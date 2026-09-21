using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

/// <summary>Finalize exactly one saved draft, including projections, in one SQLite transaction.</summary>
public sealed class EditorCommitStore(HanMateDatabase database)
{
    public async Task<ContentDocumentSnapshot> CommitAsync(SavedTextDraft draft, CancellationToken token = default)
    {
        TextDraftInput.Validate(draft.Body);
        var document = draft.Body.Annotation ?? throw new InvalidDataException("EDITOR_ANNOTATION_REQUIRED");
        PackageJson.Validate("content.schema.json", JsonSerializer.SerializeToNode(document, ContentJson.Options)!);
        if (document.Origin != ContentOrigin.Personal || document.Kind != draft.Body.Kind || document.Title != draft.Body.Title ||
            PrimaryUnit(document).Text != draft.Body.Text)
            throw new InvalidDataException("EDITOR_ANNOTATION_STALE");
        if (document.Kind == ContentKind.Grammar && (document.TextUnits.FirstOrDefault(u => u.Role == TextUnitRole.Example)?.Text != draft.Body.Example ||
            Pattern(document.Grammar!) != draft.Body.Pattern)) throw new InvalidDataException("EDITOR_ANNOTATION_STALE");
        await database.InitializeAsync(token);
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        using var guard = connection.CreateCommand(); guard.Transaction = transaction;
        guard.CommandText = "SELECT body_json FROM draft WHERE id=$id AND row_revision=$revision AND draft_kind='content'";
        guard.Parameters.AddWithValue("$id", draft.Id.ToString()); guard.Parameters.AddWithValue("$revision", draft.Revision);
        if (await guard.ExecuteScalarAsync(token) is not string saved || saved != JsonSerializer.Serialize(draft.Body, ContentJson.Options))
            throw new RevisionConflictException("draft", draft.Id.ToString(), draft.Revision);
        if (draft.Body.ExpectedContentRevision > 0)
        {
            guard.CommandText = "SELECT origin FROM content WHERE id=$content AND row_revision=$expected";
            guard.Parameters.AddWithValue("$content", document.Id.ToString()); guard.Parameters.AddWithValue("$expected", draft.Body.ExpectedContentRevision);
            if (await guard.ExecuteScalarAsync(token) is not "personal") throw new RevisionConflictException("content", document.Id.ToString(), draft.Body.ExpectedContentRevision);
        }
        var result = await new SqliteContentDocumentStore(database, new()).SaveInTransactionAsync(connection, transaction, document, draft.Body.ExpectedContentRevision, token);
        await SyncTargetsAsync(connection, transaction, document, token);
        guard.CommandText = "DELETE FROM draft WHERE id=$id AND row_revision=$revision";
        if (await guard.ExecuteNonQueryAsync(token) != 1) throw new RevisionConflictException("draft", draft.Id.ToString(), draft.Revision);
        token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None); return result;
    }

    public static string Pattern(GrammarDefinition grammar) => string.Concat(grammar.PatternParts.Select(p => p.Kind == GrammarPatternPartKind.Slot ? "{" + p.Text + "}" : p.Text));

    public static TextUnit PrimaryUnit(ContentDocument document) => document.Kind switch {
        ContentKind.Word => document.TextUnits.Single(u => u.Role == TextUnitRole.Headword),
        ContentKind.Grammar => document.TextUnits.Single(u => u.Id == document.Grammar!.ExplanationUnitIds[0]),
        _ => document.TextUnits.Single(u => u.Role == TextUnitRole.Body) };

    public async Task<SavedTextDraft> StartAsync(ContentDocumentSnapshot snapshot, CancellationToken token = default)
    {
        var document = snapshot.Document;
        var revision = snapshot.RowRevision;
        if (document.Origin != ContentOrigin.Personal) { document = Copy(document); revision = 0; }
        var body = new TextDraft(document.Title, PrimaryUnit(document).Text, document.Kind, "textDraft.v2") {
            Annotation = document, ExpectedContentRevision = revision, StructuredOnly = !CanEditText(document),
            Pattern = document.Grammar is null ? "" : Pattern(document.Grammar),
            Example = document.TextUnits.FirstOrDefault(u => u.Role == TextUnitRole.Example)?.Text ?? "" };
        return await new TextDraftStore(database).SaveAsync(Guid.NewGuid(), body, 0, token);
    }

    private static bool CanEditText(ContentDocument document) => document.Kind != ContentKind.Grammar && document.TextUnits.Count == 1;

    public static ContentDocument Copy(ContentDocument source)
    {
        var ids = new Dictionary<Guid, Guid>();
        Guid Map(Guid id) { if (!ids.TryGetValue(id, out var mapped)) ids[id] = mapped = Guid.NewGuid(); return mapped; }
        return source with { Id = Map(source.Id), Origin = ContentOrigin.Personal, ContentRevision = 1, AnnotationRevision = 1, MetadataRevision = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
            TextUnits = source.TextUnits.Select(u => u with { Id = Map(u.Id), Tokens = u.Tokens.Select(t => t with { Id = Map(t.Id) }).ToArray(),
                Segments = u.Segments.Select(s => s with { Id = Map(s.Id), TokenIds = s.TokenIds.Select(Map).ToArray() }).ToArray() }).ToArray(),
            Grammar = source.Grammar is not { } g ? null : g with { ExplanationUnitIds = g.ExplanationUnitIds.Select(Map).ToArray(),
                ExampleUnitIds = g.ExampleUnitIds.Select(Map).ToArray(), NoteUnitIds = g.NoteUnitIds.Select(Map).ToArray() } };
    }

    internal static async Task SyncTargetsAsync(SqliteConnection connection, SqliteTransaction transaction, ContentDocument document, CancellationToken token)
    {
        var desired = new HashSet<string>();
        foreach (var unit in document.TextUnits)
        {
            await Target(unit.Id, "unit", unit.Text, unit.Tokens, 0);
            var tokenMap = unit.Tokens.ToDictionary(t => t.Id);
            foreach (var segment in unit.Segments.Where(s => s.Kind == SegmentKind.Speech))
                await Target(segment.Id, "segment", segment.Text, segment.TokenIds.Select(id => tokenMap[id]), segment.Start);
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id FROM playback_target WHERE content_id=$content"; command.Parameters.AddWithValue("$content", document.Id.ToString());
        var obsolete = new List<string>();
        using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) if (!desired.Contains(reader.GetString(0))) obsolete.Add(reader.GetString(0));
        foreach (var id in obsolete)
        {
            command.Parameters.Clear(); command.Parameters.AddWithValue("$id", id);
            command.CommandText = "SELECT COUNT(*) FROM audio_binding WHERE target_id=$id";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0) throw new InvalidOperationException("EDITOR_AUDIO_TARGET_PROTECTED");
            // Audio drafts also own targets before an audio_binding exists. Do not strand a pending capture by removing its segment.
            command.CommandText = "SELECT COUNT(*) FROM draft WHERE draft_kind='audio' AND EXISTS(SELECT 1 FROM json_tree(body_json) j WHERE j.type='text' AND j.value COLLATE NOCASE=$id)";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0) throw new InvalidOperationException("EDITOR_AUDIO_TARGET_PROTECTED");
            command.CommandText = "DELETE FROM playback_target WHERE id=$id"; await command.ExecuteNonQueryAsync(token);
        }
        async Task Target(Guid id, string role, string text, IEnumerable<TextToken> tokens, int start)
        {
            var pronunciation = new JsonArray(tokens.Select(t => (JsonNode?)new JsonArray(JsonValue.Create(t.Start - start), JsonValue.Create(t.Length),
                t.Pinyin is null ? null : JsonValue.Create(t.Pinyin.Base), t.Pinyin is null ? null : JsonValue.Create(t.Pinyin.Tone), t.Pinyin is null ? null : JsonValue.Create(t.Pinyin.Erhua))).ToArray());
            var textHash = PackageJson.Hash(Encoding.UTF8.GetBytes(text)); var pronunciationHash = PackageJson.Hash(pronunciation);
            desired.Add(id.ToString()); using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO playback_target(id,content_id,role,text_hash,pronunciation_hash) VALUES($id,$content,$role,$text,$pronunciation)
                ON CONFLICT(id) DO UPDATE SET text_hash=$text,pronunciation_hash=$pronunciation
                WHERE playback_target.content_id=$content AND playback_target.role=$role;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.Parameters.AddWithValue("$content", document.Id.ToString()); cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$text", textHash); cmd.Parameters.AddWithValue("$pronunciation", pronunciationHash);
            if (await cmd.ExecuteNonQueryAsync(token) != 1) throw new InvalidDataException("EDITOR_TARGET_CONFLICT");
            cmd.CommandText = "UPDATE audio_binding SET review_state='needsReview' WHERE target_id=$id AND (bound_text_hash<>$text OR bound_pronunciation_hash<>$pronunciation)";
            await cmd.ExecuteNonQueryAsync(token);
        }
    }
}
