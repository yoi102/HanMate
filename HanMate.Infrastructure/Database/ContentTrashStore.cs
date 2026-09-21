using System.Text.Json;
using HanMate.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record ContentDependencies(int Favorites, int Audio, int Drafts, int Other);
public sealed record ContentTrashPlan(Guid ContentId, string Title, long Revision, long MembershipRevision, long Epoch, ContentDependencies Dependencies)
{
    internal string DatabasePath { get; init; } = "";
}
public sealed record TrashedContent(Guid MarkerId, Guid ContentId, string Title, string BodyJson);

/// <summary>Reversible archive markers. Content graphs, references and files remain intact; no physical purge.</summary>
public sealed class ContentTrashStore(HanMateDatabase database)
{
    internal const string Active = "NOT EXISTS(SELECT 1 FROM draft trash WHERE trash.target_content_id=c.id AND trash.draft_kind='content' AND json_extract(trash.body_json,'$.format')='contentTrash.v1')";
    internal static async Task EnsureActiveAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid id, CancellationToken token = default)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM draft WHERE target_content_id=$id AND draft_kind='content' AND json_extract(body_json,'$.format')='contentTrash.v1'";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0) throw new InvalidOperationException("CONTENT_IN_TRASH");
    }

    public async Task<ContentTrashPlan> PreviewAsync(Guid id, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        return await InspectAsync(connection, transaction, id, token);
    }
    private async Task<ContentTrashPlan> InspectAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, CancellationToken token)
    {
        await EnsureActiveAsync(connection, transaction, id, token);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            WITH identities AS (SELECT j.value AS id FROM content c,json_tree(c.body_json) j WHERE c.id=$id AND j.key='id' AND j.type='text')
            SELECT c.title,c.row_revision,c.membership_revision,(SELECT data_epoch FROM app_state WHERE singleton=1),
              (SELECT count(*) FROM favorite_item WHERE content_id=c.id),
              (SELECT count(*) FROM audio_binding b JOIN playback_target t ON t.id=b.target_id WHERE t.content_id=c.id),
              (SELECT count(*) FROM draft d WHERE d.target_content_id COLLATE NOCASE IN (SELECT id FROM identities)
                OR EXISTS(SELECT 1 FROM json_tree(d.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities))),
              (SELECT count(*) FROM import_mapping WHERE local_id COLLATE NOCASE IN (SELECT id FROM identities))
              +(SELECT count(*) FROM pinyin_item p WHERE EXISTS(SELECT 1 FROM json_tree(p.body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE IN (SELECT id FROM identities)))
            FROM content c WHERE c.id=$id AND c.origin='personal' AND NOT EXISTS(SELECT 1 FROM resource_entry e WHERE e.content_id=c.id);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("PERSONAL_CONTENT_REQUIRED");
        return new(id, reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
            new(reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7))) { DatabasePath = database.DatabasePath };
    }
    public async Task MoveAsync(ContentTrashPlan plan, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token); using var transaction = connection.BeginTransaction();
        var current = await InspectAsync(connection, transaction, plan.ContentId, token);
        if (current != plan) throw new RevisionConflictException("content-trash", plan.ContentId.ToString(), plan.Revision);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO draft(id,target_content_id,draft_kind,row_revision,body_json,updated_at_utc) VALUES($marker,$id,'content',1,$body,$time);
            UPDATE content SET row_revision=row_revision+1 WHERE id=$id;
            UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;
            """;
        command.Parameters.AddWithValue("$marker", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$id", plan.ContentId.ToString());
        command.Parameters.AddWithValue("$body", JsonSerializer.Serialize(new { format = "contentTrash.v1", dependencies = plan.Dependencies }));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token); token.ThrowIfCancellationRequested(); transaction.Commit();
    }
    public async Task<IReadOnlyList<TrashedContent>> ListAsync(int offset = 0, CancellationToken token = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        command.CommandText = "SELECT d.id,c.id,c.title,c.body_json FROM draft d JOIN content c ON c.id=d.target_content_id WHERE d.draft_kind='content' AND json_extract(d.body_json,'$.format')='contentTrash.v1' ORDER BY d.updated_at_utc DESC,d.id LIMIT 50 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", offset); var rows = new List<TrashedContent>(); using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
        return rows;
    }
    public async Task RestoreAsync(TrashedContent row, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM draft WHERE id=$marker AND target_content_id=$id AND draft_kind='content' AND json_extract(body_json,'$.format')='contentTrash.v1'";
        command.Parameters.AddWithValue("$marker", row.MarkerId.ToString()); command.Parameters.AddWithValue("$id", row.ContentId.ToString());
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new RevisionConflictException("content-trash", row.ContentId.ToString(), 0);
        command.CommandText = "UPDATE content SET row_revision=row_revision+1 WHERE id=$id; UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;";
        await command.ExecuteNonQueryAsync(token); token.ThrowIfCancellationRequested(); transaction.Commit();
    }
}
