using System.Text.Json;
using System.Text.RegularExpressions;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Core.Resources;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed class ResourceStateStore(HanMateDatabase database)
{
    private static readonly Regex EntryIdPattern = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

    public async Task<long> RegisterAsync(
        ResourceRegistration registration,
        IReadOnlyCollection<ResourceEntryLink> entries,
        ResourceOperationCommit operation,
        CancellationToken cancellationToken = default)
    {
        ValidateRegistration(registration, entries, operation);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var nextEpoch = await RegisterInTransactionAsync(connection, transaction, registration, entries, operation, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return nextEpoch;
    }

    internal static async Task<long> RegisterInTransactionAsync(
        SqliteConnection connection, SqliteTransaction transaction, ResourceRegistration registration,
        IReadOnlyCollection<ResourceEntryLink> entries, ResourceOperationCommit operation, CancellationToken cancellationToken, bool reinstall = false)
    {
        ValidateRegistration(registration, entries, operation);
        await EnsureEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
            await ValidateOwnedContentAsync(connection, transaction, registration, entry, cancellationToken).ConfigureAwait(false);

        await using (var resourceCommand = connection.CreateCommand())
        {
            resourceCommand.Transaction = transaction;
            resourceCommand.CommandText = reinstall ? """
                UPDATE installed_resource SET is_present=1,enabled=1,row_revision=row_revision+1
                WHERE resource_id=$id AND is_present=0 AND version=$version AND payload_fingerprint=$payload
                    AND distribution=$distribution AND resource_kind=$kind;
                """ : """
                INSERT INTO installed_resource(
                    resource_id,resource_kind,version,descriptor_json,descriptor_sha256,
                    payload_fingerprint,distribution,is_present,enabled,priority,row_revision)
                VALUES($id,$kind,$version,$descriptor,$descriptorSha,$payload,$distribution,$present,$enabled,$priority,1);
                """;
            resourceCommand.Parameters.AddWithValue("$id", registration.ResourceId.ToString("D"));
            resourceCommand.Parameters.AddWithValue("$kind", ToDatabaseValue(registration.Kind));
            resourceCommand.Parameters.AddWithValue("$version", registration.Version);
            resourceCommand.Parameters.AddWithValue("$descriptor", registration.DescriptorJson);
            resourceCommand.Parameters.AddWithValue("$descriptorSha", registration.DescriptorSha256.ToLowerInvariant());
            resourceCommand.Parameters.AddWithValue("$payload", registration.PayloadFingerprint.ToLowerInvariant());
            resourceCommand.Parameters.AddWithValue("$distribution", ToDatabaseValue(registration.Distribution));
            resourceCommand.Parameters.AddWithValue("$present", registration.IsPresent ? 1 : 0);
            resourceCommand.Parameters.AddWithValue("$enabled", registration.IsEnabled ? 1 : 0);
            resourceCommand.Parameters.AddWithValue("$priority", registration.Priority);
            if (await resourceCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Resource registration changed before installation.");
        }

        foreach (var entry in entries)
        {
            await using var entryCommand = connection.CreateCommand();
            entryCommand.Transaction = transaction;
            entryCommand.CommandText = "INSERT INTO resource_entry(resource_id,entry_id,content_id) VALUES($resource,$entry,$content);";
            entryCommand.Parameters.AddWithValue("$resource", entry.ResourceId.ToString("D"));
            entryCommand.Parameters.AddWithValue("$entry", entry.EntryId);
            entryCommand.Parameters.AddWithValue("$content", entry.ContentId.ToString("D"));
            await entryCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertOperationAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
        var nextEpoch = await AdvanceEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);
        return nextEpoch;
    }

    public async Task<long> SetEnabledAsync(
        Guid resourceId,
        bool enabled,
        long expectedRowRevision,
        ResourceOperationCommit operation,
        CancellationToken cancellationToken = default)
    {
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource ID must not be empty.", nameof(resourceId));
        if (expectedRowRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRowRevision));
        ValidateOperation(operation, resourceId, enabled ? ResourceOperationType.Enable : ResourceOperationType.Disable);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE installed_resource
                SET enabled=$enabled,row_revision=row_revision+1
                WHERE resource_id=$id AND row_revision=$revision AND is_present=1;
                """;
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$id", resourceId.ToString("D"));
            command.Parameters.AddWithValue("$revision", expectedRowRevision);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RevisionConflictException("installed_resource", resourceId.ToString("D"), expectedRowRevision);
        }

        await InsertOperationAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
        var nextEpoch = await AdvanceEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return nextEpoch;
    }

    public async Task<long> SetEntryRemovedAsync(
        Guid resourceId,
        string entryId,
        bool removed,
        long expectedRowRevision,
        DateTimeOffset updatedAtUtc,
        ResourceOperationCommit operation,
        CancellationToken cancellationToken = default)
    {
        ValidateEntryId(entryId);
        if (expectedRowRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRowRevision));
        ValidateOperation(operation, resourceId, removed ? ResourceOperationType.Remove : ResourceOperationType.RestoreEntry);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);

        await using (var revisionCommand = connection.CreateCommand())
        {
            revisionCommand.Transaction = transaction;
            revisionCommand.CommandText = "UPDATE installed_resource SET row_revision=row_revision+1 WHERE resource_id=$id AND row_revision=$revision AND is_present=1 AND EXISTS(SELECT 1 FROM resource_entry WHERE resource_id=$id AND entry_id=$entry);";
            revisionCommand.Parameters.AddWithValue("$entry", entryId);
            revisionCommand.Parameters.AddWithValue("$id", resourceId.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$revision", expectedRowRevision);
            if (await revisionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RevisionConflictException("installed_resource", resourceId.ToString("D"), expectedRowRevision);
        }

        await using (var overrideCommand = connection.CreateCommand())
        {
            overrideCommand.Transaction = transaction;
            overrideCommand.CommandText = """
                INSERT INTO resource_entry_override(resource_id,entry_id,removed,updated_at_utc)
                VALUES($resource,$entry,$removed,$updated)
                ON CONFLICT(resource_id,entry_id) DO UPDATE SET
                    removed=excluded.removed,updated_at_utc=excluded.updated_at_utc;
                """;
            overrideCommand.Parameters.AddWithValue("$resource", resourceId.ToString("D"));
            overrideCommand.Parameters.AddWithValue("$entry", entryId);
            overrideCommand.Parameters.AddWithValue("$removed", removed ? 1 : 0);
            overrideCommand.Parameters.AddWithValue("$updated", updatedAtUtc.ToUniversalTime().ToString("O"));
            await overrideCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertOperationAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
        var nextEpoch = await AdvanceEpochAsync(connection, transaction, operation.ExpectedDataEpoch, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return nextEpoch;
    }

    public async Task<InstalledResourceSnapshot?> GetAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource ID must not be empty.", nameof(resourceId));
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT resource_kind,version,descriptor_json,descriptor_sha256,payload_fingerprint,
                   distribution,is_present,enabled,priority,row_revision
            FROM installed_resource WHERE resource_id=$id;
            """;
        command.Parameters.AddWithValue("$id", resourceId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new InstalledResourceSnapshot(
            resourceId,
            ParseResourceKind(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ParseDistribution(reader.GetString(5)),
            reader.GetInt64(6) == 1,
            reader.GetInt64(7) == 1,
            reader.GetInt32(8),
            reader.GetInt64(9));
    }

    public async Task<IReadOnlyList<ResourceEntryLink>> GetEntriesAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT entry_id,content_id FROM resource_entry WHERE resource_id=$id ORDER BY entry_id;";
        command.Parameters.AddWithValue("$id", resourceId.ToString("D"));
        var entries = new List<ResourceEntryLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            entries.Add(new ResourceEntryLink(resourceId, reader.GetString(0), Guid.Parse(reader.GetString(1))));
        return entries;
    }

    public async Task<IReadOnlyList<Guid>> GetQueryableContentIdsAsync(ResourceKind kind, CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.content_id
            FROM resource_entry e
            JOIN installed_resource r ON r.resource_id=e.resource_id
            LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id
            WHERE r.resource_kind=$kind AND r.is_present=1 AND r.enabled=1 AND COALESCE(o.removed,0)=0
            ORDER BY r.priority,e.resource_id,e.entry_id;
            """;
        command.Parameters.AddWithValue("$kind", ToDatabaseValue(kind));
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Guid.Parse(reader.GetString(0)));
        return result;
    }

    public async Task<RetainedContentRecord?> GetRetainedAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        if (contentId == Guid.Empty) throw new ArgumentException("Content ID must not be empty.", nameof(contentId));
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_resource_id,source_entry_id,source_version,reason,retained_at_utc
            FROM retained_content WHERE content_id=$content;
            """;
        command.Parameters.AddWithValue("$content", contentId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new RetainedContentRecord(
            contentId,
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ParseRetainedReason(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void ValidateRegistration(
        ResourceRegistration registration,
        IReadOnlyCollection<ResourceEntryLink> entries,
        ResourceOperationCommit operation)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(entries);
        if (registration.ResourceId == Guid.Empty) throw new ArgumentException("Resource ID must not be empty.", nameof(registration));
        if (!VersionPattern.IsMatch(registration.Version)) throw new ArgumentException("Resource version must be strict major.minor.patch.", nameof(registration));
        if (!IsSha256(registration.DescriptorSha256) || !IsSha256(registration.PayloadFingerprint))
            throw new ArgumentException("Resource hashes must be 64 hexadecimal characters.", nameof(registration));
        if (registration.Priority < 0) throw new ArgumentOutOfRangeException(nameof(registration));
        if (!registration.IsPresent && registration.IsEnabled) throw new ArgumentException("A missing resource cannot be enabled.", nameof(registration));
        if (!registration.IsPresent && entries.Count != 0) throw new ArgumentException("A missing resource cannot own active entries.", nameof(entries));
        if (entries.Any(entry => entry.ResourceId != registration.ResourceId)) throw new ArgumentException("Entry resource IDs must match the registration.", nameof(entries));
        foreach (var entry in entries) ValidateEntryId(entry.EntryId);
        if (entries.Select(entry => entry.EntryId).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new ArgumentException("Resource entry IDs must be unique.", nameof(entries));
        if (entries.Select(entry => entry.ContentId).Distinct().Count() != entries.Count)
            throw new ArgumentException("A registration cannot own the same content more than once.", nameof(entries));
        ValidateOperation(operation, registration.ResourceId, ResourceOperationType.Install);
        ValidateDescriptorProjection(registration, entries);
    }

    private static void ValidateDescriptorProjection(ResourceRegistration registration, IReadOnlyCollection<ResourceEntryLink> entries)
    {
        using var descriptor = JsonDocument.Parse(registration.DescriptorJson);
        var root = descriptor.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || root.GetProperty("resourceId").GetGuid() != registration.ResourceId
            || !string.Equals(root.GetProperty("version").GetString(), registration.Version, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("resourceKind").GetString(), ToDatabaseValue(registration.Kind), StringComparison.Ordinal))
            throw new ArgumentException("Descriptor identity does not match the resource registration.", nameof(registration));

        if (!registration.IsPresent) return;
        var declared = root.GetProperty("entries").EnumerateArray()
            .Select(item => (EntryId: item.GetProperty("entryId").GetString()!, ContentId: item.GetProperty("contentId").GetGuid()))
            .ToHashSet();
        var projected = entries.Select(entry => (entry.EntryId, entry.ContentId)).ToHashSet();
        if (declared.Count != entries.Count || !declared.SetEquals(projected))
            throw new ArgumentException("An installed resource must project exactly the descriptor entry set.", nameof(entries));
    }

    private static async Task ValidateOwnedContentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ResourceRegistration registration,
        ResourceEntryLink entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT kind,origin,body_json FROM content WHERE id=$id;";
        command.Parameters.AddWithValue("$id", entry.ContentId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Resource entry '{entry.EntryId}' references missing content '{entry.ContentId:D}'.");
        if (!string.Equals(reader.GetString(1), "resource", StringComparison.Ordinal))
            throw new InvalidOperationException($"Resource entry '{entry.EntryId}' must own content with origin=resource.");
        if (registration.Kind == ResourceKind.Dictionary && !string.Equals(reader.GetString(0), "word", StringComparison.Ordinal))
            throw new InvalidOperationException("Dictionary resources can own word content only.");

        var document = JsonSerializer.Deserialize<ContentDocument>(reader.GetString(2), ContentJson.Options)
            ?? throw new JsonException("Persisted content document is null.");
        if (document.Source.ResourceId != registration.ResourceId
            || !string.Equals(document.Source.ResourceVersion, registration.Version, StringComparison.Ordinal)
            || !string.Equals(document.Source.EntryId, entry.EntryId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Resource entry '{entry.EntryId}' does not match ContentDocument source identity.");
    }

    private static void ValidateOperation(ResourceOperationCommit operation, Guid resourceId, ResourceOperationType expectedType)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.OperationId == Guid.Empty) throw new ArgumentException("Operation ID must not be empty.", nameof(operation));
        if (operation.ResourceId != resourceId) throw new ArgumentException("Operation resource ID does not match.", nameof(operation));
        if (operation.OperationType != expectedType) throw new ArgumentException($"Operation type must be {expectedType}.", nameof(operation));
        if (operation.ExpectedDataEpoch < 1) throw new ArgumentOutOfRangeException(nameof(operation));
        using var _ = JsonDocument.Parse(operation.ResultJson);
    }

    private static async Task EnsureEpochAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long expectedEpoch,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data_epoch FROM app_state WHERE singleton=1;";
        var actual = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (actual != expectedEpoch) throw new DataEpochConflictException(expectedEpoch, actual);
    }

    private static async Task<long> AdvanceEpochAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long expectedEpoch,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1 AND data_epoch=$expected RETURNING data_epoch;";
        command.Parameters.AddWithValue("$expected", expectedEpoch);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null) throw new DataEpochConflictException(expectedEpoch, -1);
        return Convert.ToInt64(result);
    }

    private static async Task InsertOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ResourceOperationCommit operation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO resource_operation(operation_id,resource_id,operation_type,expected_data_epoch,committed_at_utc,result_json)
            VALUES($operation,$resource,$type,$epoch,$committed,$result);
            """;
        command.Parameters.AddWithValue("$operation", operation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$resource", operation.ResourceId.ToString("D"));
        command.Parameters.AddWithValue("$type", ToDatabaseValue(operation.OperationType));
        command.Parameters.AddWithValue("$epoch", operation.ExpectedDataEpoch);
        command.Parameters.AddWithValue("$committed", operation.CommittedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$result", operation.ResultJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSha256(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void ValidateEntryId(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (!EntryIdPattern.IsMatch(entryId)) throw new ArgumentException("Entry ID is outside the resource schema.", nameof(entryId));
    }

    private static string ToDatabaseValue(ResourceKind kind) => kind == ResourceKind.Learning ? "learning" : "dictionary";
    private static string ToDatabaseValue(ResourceDistribution distribution) => distribution == ResourceDistribution.Bundled ? "bundled" : "external";
    private static string ToDatabaseValue(ResourceOperationType type) => type switch
    {
        ResourceOperationType.Install => "install",
        ResourceOperationType.Update => "update",
        ResourceOperationType.Remove => "remove",
        ResourceOperationType.RestoreEntry => "restoreEntry",
        ResourceOperationType.Disable => "disable",
        ResourceOperationType.Enable => "enable",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static ResourceKind ParseResourceKind(string value) => value switch
    {
        "learning" => ResourceKind.Learning,
        "dictionary" => ResourceKind.Dictionary,
        _ => throw new InvalidDataException($"Unknown resource kind '{value}'.")
    };

    private static ResourceDistribution ParseDistribution(string value) => value switch
    {
        "bundled" => ResourceDistribution.Bundled,
        "external" => ResourceDistribution.External,
        _ => throw new InvalidDataException($"Unknown resource distribution '{value}'.")
    };

    private static RetainedReason ParseRetainedReason(string value) => value switch
    {
        "favorite" => RetainedReason.Favorite,
        "userAudio" => RetainedReason.UserAudio,
        "draft" => RetainedReason.Draft,
        "explicitKeep" => RetainedReason.ExplicitKeep,
        "multiple" => RetainedReason.Multiple,
        _ => throw new InvalidDataException($"Unknown retained-content reason '{value}'.")
    };
}

public sealed class DataEpochConflictException(long expectedEpoch, long actualEpoch)
    : Exception($"Resource plan epoch {expectedEpoch} is stale; current epoch is {actualEpoch}.")
{
    public long ExpectedEpoch { get; } = expectedEpoch;
    public long ActualEpoch { get; } = actualEpoch;
}
