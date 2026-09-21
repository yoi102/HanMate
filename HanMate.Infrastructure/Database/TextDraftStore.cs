using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;

namespace HanMate.Infrastructure.Database;

public sealed record SavedTextDraft(Guid Id, TextDraft Body, long Revision, DateTimeOffset Updated);
public sealed class TextDraftStore(HanMateDatabase database)
{
    public async Task<IReadOnlyList<SavedTextDraft>> ListAsync(int offset = 0, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,body_json,row_revision,updated_at_utc FROM draft WHERE draft_kind='content' AND json_extract(body_json,'$.format') IN ('textDraft.v1','textDraft.v2') ORDER BY updated_at_utc DESC,id LIMIT 50 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", offset); using var reader = await command.ExecuteReaderAsync(token); var rows = new List<SavedTextDraft>();
        while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), JsonSerializer.Deserialize<TextDraft>(reader.GetString(1), ContentJson.Options)!, reader.GetInt64(2), DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }
    public async Task<SavedTextDraft> SaveAsync(Guid id, TextDraft body, long expectedRevision, CancellationToken token = default)
    {
        TextDraftInput.Validate(body); var now = DateTimeOffset.UtcNow;
        await new VersionedLocalStateStore(database).SaveDraftAsync(id, body.ExpectedContentRevision > 0 ? body.Annotation?.Id : null, "content", JsonSerializer.Serialize(body, ContentJson.Options), expectedRevision, now, token);
        return new(id, body, expectedRevision + 1, now);
    }
    public async Task DeleteAsync(SavedTextDraft expected, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM draft WHERE id=$id AND row_revision=$revision AND json_extract(body_json,'$.format') IN ('textDraft.v1','textDraft.v2')";
        command.Parameters.AddWithValue("$id", expected.Id.ToString()); command.Parameters.AddWithValue("$revision", expected.Revision);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new RevisionConflictException("draft", expected.Id.ToString(), expected.Revision);
    }
}
