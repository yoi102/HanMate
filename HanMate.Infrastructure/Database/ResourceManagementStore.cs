using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record ManagedResource(InstalledResourceSnapshot State, int Count, int RemovedCount, long TextBytes, long Epoch)
{
    public string Name(string language)
    {
        using var json = JsonDocument.Parse(State.DescriptorJson);
        return json.RootElement.TryGetProperty("names", out var names) && names.TryGetProperty(language, out var name)
            ? name.GetString() ?? State.ResourceId.ToString() : State.ResourceId.ToString();
    }
}
public sealed class ResourceUninstallPlan
{
    internal ResourceUninstallPlan(string databasePath, ManagedResource resource, int favorites, int audio, int drafts, int other)
    { DatabasePath = databasePath; Resource = resource; Favorites = favorites; Audio = audio; Drafts = drafts; Other = other; }
    internal string DatabasePath { get; }
    public ManagedResource Resource { get; }
    public int Favorites { get; }
    public int Audio { get; }
    public int Drafts { get; }
    public int Other { get; }
    public bool CanUninstall => Resource.State.IsPresent && Favorites + Audio + Drafts + Other == 0;
}

/// <summary>Text-only lifecycle. Referenced/modified graphs are preserved until retained-content migration is available.</summary>
public sealed class ResourceManagementStore(HanMateDatabase database)
{
    public sealed record Entry(string EntryId, Guid ContentId, string Title, string BodyJson, bool Removed);
    public sealed record RetentionEntry(string EntryId, Guid ContentId, string BodyJson, bool Keep);
    public sealed record RetentionPlan(ManagedResource Resource, IReadOnlyList<RetentionEntry> Entries)
    {
        internal string DatabasePath { get; init; } = "";
        public int RetainedCount => Entries.Count(e => e.Keep);
        public int DeletedCount => Entries.Count - RetainedCount;
    }

    public async Task<IReadOnlyList<Entry>> GetEntriesAsync(Guid resourceId, int offset = 0, CancellationToken token = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        await database.InitializeAsync(token); using var c = await database.OpenConnectionAsync(token); using var command = c.CreateCommand();
        command.CommandText = "SELECT e.entry_id,c.id,c.title,c.body_json,COALESCE(o.removed,0) FROM resource_entry e JOIN content c ON c.id=e.content_id LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id WHERE e.resource_id=$id ORDER BY e.entry_id LIMIT 50 OFFSET $offset";
        command.Parameters.AddWithValue("$id", resourceId.ToString()); command.Parameters.AddWithValue("$offset", offset);
        using var reader = await command.ExecuteReaderAsync(token); var rows = new List<Entry>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetString(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) != 0));
        return rows;
    }
    public async Task<IReadOnlyList<LearningRow>> GetRetainedAsync(int offset = 0, CancellationToken token = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        await database.InitializeAsync(token); using var c = await database.OpenConnectionAsync(token); using var command = c.CreateCommand();
        command.CommandText = "SELECT c.id,c.title,c.body_json FROM retained_content r JOIN content c ON c.id=r.content_id ORDER BY r.retained_at_utc DESC,c.id LIMIT 50 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", offset); using var reader = await command.ExecuteReaderAsync(token); var rows = new List<LearningRow>();
        while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return rows;
    }
    public async Task<RetentionPlan> PreviewRetainingUninstallAsync(ManagedResource resource, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var c = await database.OpenConnectionAsync(token); using var tx = c.BeginTransaction(deferred: true);
        await CheckAsync(c, tx, resource, token); return await RetentionAsync(c, tx, resource, token);
    }
    private async Task<RetentionPlan> RetentionAsync(SqliteConnection c, SqliteTransaction tx, ManagedResource resource, CancellationToken token)
    {
        using var descriptor = JsonDocument.Parse(resource.State.DescriptorJson);
        if (!resource.State.IsPresent)
            throw new PackageException("RESOURCE_DEPENDENCIES_EXIST");
        using var command = c.CreateCommand(); command.Transaction = tx;
        // Resource-ID references conservatively retain every entry; graph-ID references retain their exact owner.
        command.CommandText = """
            SELECT e.entry_id,c.id,c.body_json,
              c.origin<>'resource' OR c.row_revision>1
              OR EXISTS(SELECT 1 FROM favorite_item WHERE content_id=c.id)
              OR EXISTS(SELECT 1 FROM audio_binding b JOIN playback_target t ON t.id=b.target_id WHERE t.content_id=c.id)
              OR EXISTS(SELECT 1 FROM retained_content WHERE content_id=c.id)
              OR EXISTS(SELECT 1 FROM draft d WHERE d.target_content_id COLLATE NOCASE IN
                  (SELECT $id UNION SELECT j.value FROM json_tree(c.body_json) j WHERE j.key='id' AND j.type='text')
                OR EXISTS(SELECT 1 FROM json_tree(d.body_json) v WHERE v.type='text' AND v.value COLLATE NOCASE IN
                  (SELECT $id UNION SELECT j.value FROM json_tree(c.body_json) j WHERE j.key='id' AND j.type='text')))
              OR EXISTS(SELECT 1 FROM import_mapping m WHERE m.local_id COLLATE NOCASE IN
                  (SELECT $id UNION SELECT j.value FROM json_tree(c.body_json) j WHERE j.key='id' AND j.type='text'))
              OR EXISTS(SELECT 1 FROM pinyin_item p,json_tree(p.body_json) v WHERE v.type='text' AND v.value COLLATE NOCASE IN
                  (SELECT $id UNION SELECT j.value FROM json_tree(c.body_json) j WHERE j.key='id' AND j.type='text'))
            FROM resource_entry e JOIN content c ON c.id=e.content_id WHERE e.resource_id=$id ORDER BY e.entry_id;
            """;
        command.Parameters.AddWithValue("$id", resource.State.ResourceId.ToString());
        using var reader = await command.ExecuteReaderAsync(token); var entries = new List<RetentionEntry>();
        while (await reader.ReadAsync(token)) entries.Add(new(reader.GetString(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3) != 0));
        if (entries.Count != descriptor.RootElement.GetProperty("entries").GetArrayLength()) throw new PackageException("RESOURCE_INCOMPLETE");
        return new(resource, entries) { DatabasePath = database.DatabasePath };
    }
    public async Task UninstallRetainingAsync(RetentionPlan plan, CancellationToken token = default)
    {
        if (plan.DatabasePath != database.DatabasePath) throw new PackageException("PLAN_STALE");
        await database.InitializeAsync(token); using var c = await database.OpenConnectionAsync(token); using var tx = c.BeginTransaction();
        await CheckAsync(c, tx, plan.Resource, token);
        var current = await RetentionAsync(c, tx, plan.Resource, token);
        if (!current.Entries.SequenceEqual(plan.Entries)) throw new PackageException("PLAN_STALE");
        foreach (var entry in current.Entries)
        {
            using var command = c.CreateCommand(); command.Transaction = tx;
            command.Parameters.AddWithValue("$id", entry.ContentId.ToString());
            if (entry.Keep)
            {
                using var reasonCommand = c.CreateCommand(); reasonCommand.Transaction = tx;
                reasonCommand.CommandText = """
                    WITH identities AS (SELECT $resource AS id UNION SELECT j.value FROM content c,json_tree(c.body_json) j WHERE c.id=$id AND j.key='id' AND j.type='text')
                    SELECT EXISTS(SELECT 1 FROM favorite_item WHERE content_id=$id),
                      EXISTS(SELECT 1 FROM audio_binding b JOIN playback_target t ON t.id=b.target_id WHERE t.content_id=$id),
                      EXISTS(SELECT 1 FROM draft d WHERE d.target_content_id COLLATE NOCASE IN (SELECT id FROM identities)
                        OR EXISTS(SELECT 1 FROM json_tree(d.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities))),
                      EXISTS(SELECT 1 FROM content WHERE id=$id AND (origin<>'resource' OR row_revision>1))
                        OR EXISTS(SELECT 1 FROM retained_content WHERE content_id=$id)
                        OR EXISTS(SELECT 1 FROM import_mapping WHERE local_id COLLATE NOCASE IN (SELECT id FROM identities))
                        OR EXISTS(SELECT 1 FROM pinyin_item p,json_tree(p.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities));
                    """;
                reasonCommand.Parameters.AddWithValue("$id", entry.ContentId.ToString()); reasonCommand.Parameters.AddWithValue("$resource", plan.Resource.State.ResourceId.ToString());
                string reason;
                using (var reader = await reasonCommand.ExecuteReaderAsync(token))
                {
                    await reader.ReadAsync(token); var flags = Enumerable.Range(0, 4).Select(i => reader.GetInt32(i) != 0).ToArray();
                    reason = flags.Count(f => f) > 1 ? "multiple" : flags[0] ? "favorite" : flags[1] ? "userAudio" : flags[2] ? "draft" : "explicitKeep";
                }
                var document = JsonSerializer.Deserialize<ContentDocument>(entry.BodyJson, ContentJson.Options)! with { Origin = ContentOrigin.Retained };
                var body = JsonSerializer.Serialize(document, ContentJson.Options);
                command.CommandText = """
                    UPDATE content SET origin='retained',body_json=$body,semantic_fingerprint=$hash,row_revision=row_revision+1 WHERE id=$id;
                    INSERT INTO retained_content(content_id,source_resource_id,source_entry_id,source_version,reason,retained_at_utc)
                    VALUES($id,$resource,$entry,$version,$reason,$time)
                    ON CONFLICT(content_id) DO UPDATE SET source_resource_id=$resource,source_entry_id=$entry,source_version=$version,reason=$reason,retained_at_utc=$time;
                    DELETE FROM resource_entry WHERE content_id=$id;
                    """;
                command.Parameters.AddWithValue("$body", body); command.Parameters.AddWithValue("$hash", PackageJson.ContentFingerprint(JsonNode.Parse(body)!));
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue("$resource", plan.Resource.State.ResourceId.ToString()); command.Parameters.AddWithValue("$entry", entry.EntryId);
                command.Parameters.AddWithValue("$version", plan.Resource.State.Version); command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            }
            else command.CommandText = "DELETE FROM resource_entry WHERE content_id=$id; DELETE FROM content WHERE id=$id;";
            await command.ExecuteNonQueryAsync(token);
        }
        using (var command = c.CreateCommand())
        {
            command.Transaction = tx; command.CommandText = "UPDATE installed_resource SET is_present=0,enabled=0,row_revision=row_revision+1 WHERE resource_id=$id";
            command.Parameters.AddWithValue("$id", plan.Resource.State.ResourceId.ToString()); await command.ExecuteNonQueryAsync(token);
        }
        await CommitAsync(c, tx, plan.Resource, "remove", JsonSerializer.Serialize(new { action = "retainAndUninstall", retained = current.RetainedCount, deleted = current.DeletedCount }), token);
    }
    public async Task<IReadOnlyList<ManagedResource>> GetAsync(ResourceKind? kind = null, CancellationToken token = default)
    {
        await database.InitializeAsync(token).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.resource_id,r.resource_kind,r.version,r.descriptor_json,r.descriptor_sha256,r.payload_fingerprint,
                r.distribution,r.is_present,r.enabled,r.priority,r.row_revision,
                (SELECT count(*) FROM resource_entry e WHERE e.resource_id=r.resource_id),
                (SELECT count(*) FROM resource_entry_override o WHERE o.resource_id=r.resource_id AND o.removed=1),
                COALESCE((SELECT sum(length(CAST(c.body_json AS BLOB))) FROM resource_entry e JOIN content c ON c.id=e.content_id WHERE e.resource_id=r.resource_id),0),
                (SELECT data_epoch FROM app_state WHERE singleton=1)
            FROM installed_resource r WHERE $kind IS NULL OR r.resource_kind=$kind ORDER BY r.priority,r.resource_id;
            """;
        command.Parameters.AddWithValue("$kind", kind is null ? DBNull.Value : kind == ResourceKind.Dictionary ? "dictionary" : "learning");
        var result = new List<ManagedResource>();
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new(new(
            Guid.Parse(reader.GetString(0)), reader.GetString(1) == "dictionary" ? ResourceKind.Dictionary : ResourceKind.Learning,
            reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6) == "bundled" ? ResourceDistribution.Bundled : ResourceDistribution.External,
            reader.GetInt32(7) == 1, reader.GetInt32(8) == 1, reader.GetInt32(9), reader.GetInt64(10)),
            reader.GetInt32(11), reader.GetInt32(12), reader.GetInt64(13), reader.GetInt64(14)));
        return result;
    }

    public async Task SetPriorityAsync(ManagedResource resource, int priority, CancellationToken token = default)
    {
        if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority));
        await database.InitializeAsync(token).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(token).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await CheckAsync(connection, transaction, resource, token).ConfigureAwait(false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE installed_resource SET priority=$priority,row_revision=row_revision+1 WHERE resource_id=$id;";
        command.Parameters.AddWithValue("$id", resource.State.ResourceId.ToString("D")); command.Parameters.AddWithValue("$priority", priority);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await CommitAsync(connection, transaction, resource, "update", JsonSerializer.Serialize(new { action = "setPriority", priority }), token).ConfigureAwait(false);
    }

    public async Task<ResourceUninstallPlan> PreviewUninstallAsync(ManagedResource resource, CancellationToken token = default)
    {
        await database.InitializeAsync(token).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(token).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: true);
        await CheckAsync(connection, transaction, resource, token).ConfigureAwait(false);
        return await InspectAsync(connection, transaction, resource, token).ConfigureAwait(false);
    }

    public async Task UninstallAsync(ResourceUninstallPlan plan, CancellationToken token = default)
    {
        if (plan.DatabasePath != database.DatabasePath) throw new PackageException("PLAN_STALE");
        await database.InitializeAsync(token).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(token).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await CheckAsync(connection, transaction, plan.Resource, token).ConfigureAwait(false);
        // Re-scan inside the write transaction: favorites/drafts may change without advancing resource epoch.
        var current = await InspectAsync(connection, transaction, plan.Resource, token).ConfigureAwait(false);
        if (!current.CanUninstall) throw new PackageException("RESOURCE_DEPENDENCIES_EXIST");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TEMP TABLE removing_content AS SELECT content_id FROM resource_entry WHERE resource_id=$id;
            DELETE FROM resource_entry WHERE resource_id=$id;
            DELETE FROM content WHERE id IN (SELECT content_id FROM removing_content);
            UPDATE installed_resource SET is_present=0,enabled=0,row_revision=row_revision+1 WHERE resource_id=$id;
            """;
        command.Parameters.AddWithValue("$id", plan.Resource.State.ResourceId.ToString("D"));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await CommitAsync(connection, transaction, plan.Resource, "remove", JsonSerializer.Serialize(new { action = "uninstallTextResource", contentCount = current.Resource.Count }), token).ConfigureAwait(false);
    }

    private async Task<ResourceUninstallPlan> InspectAsync(SqliteConnection connection, SqliteTransaction transaction, ManagedResource resource, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            WITH owned AS (SELECT c.* FROM resource_entry e JOIN content c ON c.id=e.content_id WHERE e.resource_id=$id),
            identities AS (SELECT $id AS id UNION SELECT j.value FROM owned c,json_tree(c.body_json) j WHERE j.key='id' AND j.type='text')
            SELECT
              (SELECT count(*) FROM favorite_item WHERE content_id IN (SELECT id FROM owned)),
              (SELECT count(*) FROM audio_binding b JOIN playback_target t ON t.id=b.target_id WHERE t.content_id IN (SELECT id FROM owned)),
              (SELECT count(*) FROM draft d WHERE d.target_content_id COLLATE NOCASE IN (SELECT id FROM identities)
                OR EXISTS(SELECT 1 FROM json_tree(d.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities))),
              (SELECT count(*) FROM owned WHERE origin<>'resource' OR row_revision>1)
                +(SELECT count(*) FROM retained_content WHERE content_id IN (SELECT id FROM owned))
                +(SELECT count(*) FROM import_mapping WHERE local_id COLLATE NOCASE IN (SELECT id FROM identities))
                +(SELECT count(*) FROM pinyin_item p WHERE EXISTS(SELECT 1 FROM json_tree(p.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities))),
              (SELECT count(*) FROM owned);
            """;
        command.Parameters.AddWithValue("$id", resource.State.ResourceId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false); await reader.ReadAsync(token).ConfigureAwait(false);
        using var descriptor = JsonDocument.Parse(resource.State.DescriptorJson);
        var declared = descriptor.RootElement.GetProperty("entries").GetArrayLength();
        var declaredAudio = descriptor.RootElement.TryGetProperty("audioBindingIds", out var audio) ? audio.GetArrayLength() : 0;
        var other = reader.GetInt32(3) + (reader.GetInt32(4) != declared ? 1 : 0);
        return new(database.DatabasePath, resource, reader.GetInt32(0), reader.GetInt32(1) + declaredAudio, reader.GetInt32(2), other);
    }

    private static async Task CheckAsync(SqliteConnection connection, SqliteTransaction transaction, ManagedResource resource, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM installed_resource r,app_state s WHERE r.resource_id=$id AND r.row_revision=$revision AND s.data_epoch=$epoch;";
        command.Parameters.AddWithValue("$id", resource.State.ResourceId.ToString("D"));
        command.Parameters.AddWithValue("$revision", resource.State.RowRevision); command.Parameters.AddWithValue("$epoch", resource.Epoch);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) != 1) throw new PackageException("PLAN_STALE");
    }
    private static async Task CommitAsync(SqliteConnection connection, SqliteTransaction transaction, ManagedResource resource, string type, string result, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO resource_operation VALUES($operation,$id,$type,$epoch,$time,$result);
            UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;
            """;
        command.Parameters.AddWithValue("$operation", Guid.NewGuid().ToString("D")); command.Parameters.AddWithValue("$id", resource.State.ResourceId.ToString("D"));
        command.Parameters.AddWithValue("$type", type); command.Parameters.AddWithValue("$epoch", resource.Epoch);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$result", result);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
}
