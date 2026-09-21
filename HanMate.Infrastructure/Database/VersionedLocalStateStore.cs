using System.Text.Json;
using HanMate.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record VersionedJsonSnapshot(string Id, string BodyJson, long RowRevision, DateTimeOffset? UpdatedAtUtc = null);

public sealed class VersionedLocalStateStore(HanMateDatabase database)
{
    public async Task<VersionedJsonSnapshot?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body_json,row_revision,updated_at_utc FROM draft WHERE id=$id;";
        command.Parameters.AddWithValue("$id", draftId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new VersionedJsonSnapshot(
            draftId.ToString("D"),
            reader.GetString(0),
            reader.GetInt64(1),
            DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<VersionedJsonSnapshot?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body_json,row_revision FROM user_settings WHERE singleton=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new VersionedJsonSnapshot("1", reader.GetString(0), reader.GetInt64(1));
    }

    public async Task<VersionedJsonSnapshot> SaveDraftAsync(
        Guid draftId,
        Guid? targetContentId,
        string draftKind,
        string bodyJson,
        long expectedRevision,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (draftKind is not ("content" or "audio")) throw new ArgumentOutOfRangeException(nameof(draftKind));
        ValidateJson(bodyJson);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = expectedRevision == 0
            ? "INSERT INTO draft(id,target_content_id,draft_kind,row_revision,body_json,updated_at_utc) VALUES($id,$target, $kind,1,$body,$updated);"
            : "UPDATE draft SET target_content_id=$target,draft_kind=$kind,row_revision=row_revision+1,body_json=$body,updated_at_utc=$updated WHERE id=$id AND row_revision=$expected;";
        command.Parameters.AddWithValue("$id", draftId.ToString("D"));
        command.Parameters.AddWithValue("$target", targetContentId is null ? DBNull.Value : targetContentId.Value.ToString("D"));
        command.Parameters.AddWithValue("$kind", draftKind);
        command.Parameters.AddWithValue("$body", bodyJson);
        command.Parameters.AddWithValue("$updated", updatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$expected", expectedRevision);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RevisionConflictException("draft", draftId.ToString("D"), expectedRevision);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new RevisionConflictException("draft", draftId.ToString("D"), expectedRevision);
        }

        return new VersionedJsonSnapshot(draftId.ToString("D"), bodyJson, expectedRevision + 1, updatedAtUtc.ToUniversalTime());
    }

    public async Task<VersionedJsonSnapshot> SaveSettingsAsync(
        string bodyJson,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateJson(bodyJson);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = expectedRevision == 0
            ? "INSERT INTO user_settings(singleton,schema_version,row_revision,body_json) VALUES(1,1,1,$body);"
            : "UPDATE user_settings SET row_revision=row_revision+1,body_json=$body WHERE singleton=1 AND row_revision=$expected;";
        command.Parameters.AddWithValue("$body", bodyJson);
        command.Parameters.AddWithValue("$expected", expectedRevision);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RevisionConflictException("settings", "1", expectedRevision);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new RevisionConflictException("settings", "1", expectedRevision);
        }

        return new VersionedJsonSnapshot("1", bodyJson, expectedRevision + 1);
    }

    private static void ValidateJson(string bodyJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyJson);
        using var _ = JsonDocument.Parse(bodyJson);
    }
}
