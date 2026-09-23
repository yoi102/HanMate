using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Pinyin;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record PersonalWordExample(string Text, Guid? UnitId = null);
public sealed record WordPinyinCorrection(int Start, string? Pronunciation);
public sealed record PersonalWordInput(string Category, string Word, string Definition,
    IReadOnlyList<PersonalWordExample> Examples, string English, string Japanese, bool UseRecording)
{
    public IReadOnlyList<WordPinyinCorrection> PinyinCorrections { get; init; } = [];
}

/// <summary>Save a complete personal word and its playback targets in one transaction.</summary>
public sealed class PersonalWordStore(HanMateDatabase database)
{
    public const string AutoVoiceScene = "word-voice-auto";

    public async Task<ContentDocument> SaveAsync(PersonalWordInput input, Guid? existingId = null,
        CancellationToken token = default, Guid? replaceResourceId = null)
    {
        if (existingId is not null && replaceResourceId is not null) throw new ArgumentException("Choose one source word.");
        var word = input.Word.Trim(); var definition = input.Definition.Trim();
        var examples = input.Examples.Select(x => x with { Text = x.Text.Trim() }).Where(x => x.Text.Length > 0).ToArray();
        if (word.Length is < 1 or > 64 || definition.Length is < 1 or > 2000 || examples.Length > 20 ||
            examples.Any(x => x.Text.Length > 2000) ||
            input.English.Length > 1000 || input.Japanese.Length > 1000) throw new InvalidDataException("WORD_FIELDS_INVALID");
        var categories = await new CustomWordCategoryStore(new VersionedLocalStateStore(database)).ListAsync(token);
        if (input.Category != WordCategories.Other && !WordCategories.All.Contains(input.Category) &&
            !categories.Any(x => x.Id == input.Category))
            throw new InvalidDataException("CATEGORY_MISSING");
        var store = new SqliteContentDocumentStore(database, new());
        var old = existingId is { } id ? await store.GetAsync(id, token) : null;
        var resource = replaceResourceId is { } resourceId ? await store.GetAsync(resourceId, token) : null;
        if (existingId is not null && (old is null || old.Document.Origin != ContentOrigin.Personal ||
            old.Document.Source.SourceId != "personal" || old.Document.Kind != ContentKind.Word))
            throw new InvalidDataException("WORD_NOT_EDITABLE");
        if (replaceResourceId is not null && (resource is null || resource.Document.Origin != ContentOrigin.Resource ||
            resource.Document.Kind != ContentKind.Word)) throw new InvalidDataException("WORD_NOT_EDITABLE");
        var source = old?.Document;
        var previousHeadword = source?.TextUnits.Single(x => x.Role == TextUnitRole.Headword);
        // A changed word must get fresh context-based readings, even when the old characters were manually locked.
        var annotation = source is null || previousHeadword!.Text == word ? source : source with
        {
            TextUnits = source.TextUnits.Select(unit => unit.Role != TextUnitRole.Headword ? unit : unit with
            {
                Tokens = unit.Tokens.Select(t => t with { Locked = false,
                    ReviewState = t.Kind == TokenKind.Hanzi ? AnnotationReviewState.NeedsReview : t.ReviewState }).ToArray()
            }).ToArray()
        };
        var head = DraftAnnotation.Generate(new TextDraft(word, word, ContentKind.Word)
            { Annotation = annotation }, BundledAnnotationLexicon.Default.Engine, token).Document;
        if (input.PinyinCorrections.Select(x => x.Start).Distinct().Count() != input.PinyinCorrections.Count)
            throw new InvalidDataException("WORD_PINYIN_DUPLICATE");
        foreach (var correction in input.PinyinCorrections)
        {
            var target = head.TextUnits[0].Tokens.SingleOrDefault(x => x.Start == correction.Start && x.Kind == TokenKind.Hanzi)
                ?? throw new InvalidDataException("WORD_PINYIN_TARGET_INVALID");
            head = DraftAnnotation.Correct(head, target.Id, correction.Pronunciation);
        }
        var units = new List<TextUnit> { head.TextUnits[0] with
        {
            Translations = Translation(input.English, input.Japanese)
        } };
        units.Add(Unit(TextUnitRole.Definition, definition, source?.TextUnits.FirstOrDefault(x => x.Role == TextUnitRole.Definition), source, token));
        var usedIds = new HashSet<Guid>();
        foreach (var example in examples)
        {
            if (example.UnitId is { } exampleId && !usedIds.Add(exampleId)) throw new InvalidDataException("WORD_EXAMPLE_DUPLICATE");
            var previous = example.UnitId is { } previousId
                ? source?.TextUnits.SingleOrDefault(x => x.Id == previousId && x.Role == TextUnitRole.Example) : null;
            if (example.UnitId is not null && previous is null) throw new InvalidDataException("WORD_EXAMPLE_INVALID");
            units.Add(Unit(TextUnitRole.Example, example.Text, previous, source, token));
        }
        // Keep a bundled word's other teaching categories when the user edits it in one of them.
        // A category change on a personal word (or an explicit move of a bundled word) replaces its membership.
        var keepResourceCategories = resource is not null && input.Category != WordCategories.Other &&
            resource.Document.Scenes.Contains(WordCategories.Scene(input.Category));
        var scenes = (source?.Scenes ?? resource?.Document.Scenes ?? [])
            .Where(x => x != AutoVoiceScene && (keepResourceCategories || !x.StartsWith("word-", StringComparison.Ordinal)))
            .ToList();
        if (input.Category != WordCategories.Other && !scenes.Contains(WordCategories.Scene(input.Category)))
            scenes.Add(WordCategories.Scene(input.Category));
        if (!input.UseRecording) scenes.Add(AutoVoiceScene);
        var document = head with { Scenes = scenes, TextUnits = units, Title = word };
        await database.InitializeAsync(token);
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await store.SaveInTransactionAsync(connection, transaction, document, old?.RowRevision ?? 0, token);
        await EditorCommitStore.SyncTargetsAsync(connection, transaction, document, token);
        if (replaceResourceId is { } replaced)
        {
            await MoveFavoritesInTransactionAsync(connection, transaction, replaced, document.Id, token);
            await HideResourceInTransactionAsync(connection, transaction, replaced, ContentKind.Word, token);
        }
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
        return document;
    }

    internal static async Task MoveFavoritesInTransactionAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid sourceId, Guid replacementId, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO favorite_item(folder_id,content_id,sort_order,added_at_utc)
            SELECT folder_id,$replacement,sort_order,added_at_utc FROM favorite_item WHERE content_id=$source
            ON CONFLICT(folder_id,content_id) DO NOTHING;
            DELETE FROM favorite_item WHERE content_id=$source;
            UPDATE content SET membership_revision=membership_revision+1 WHERE id IN ($source,$replacement);
            """;
        command.Parameters.AddWithValue("$source", sourceId.ToString("D"));
        command.Parameters.AddWithValue("$replacement", replacementId.ToString("D"));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task HideResourceAsync(Guid contentId, CancellationToken token = default)
    {
        await database.InitializeAsync(token);
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await HideResourceInTransactionAsync(connection, transaction, contentId, ContentKind.Word, token);
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
    }

    internal static async Task HideResourceInTransactionAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid contentId, ContentKind kind, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            SELECT e.resource_id,e.entry_id,r.row_revision,(SELECT data_epoch FROM app_state WHERE singleton=1)
            FROM resource_entry e JOIN installed_resource r ON r.resource_id=e.resource_id
            JOIN content c ON c.id=e.content_id
            LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id
            WHERE e.content_id=$content AND c.origin='resource' AND c.kind=$kind AND
                r.resource_kind='learning' AND r.is_present=1 AND r.enabled=1 AND COALESCE(o.removed,0)=0
            """;
        command.Parameters.AddWithValue("$content", contentId.ToString("D"));
        command.Parameters.AddWithValue("$kind", kind.ToString().ToLowerInvariant());
        using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidDataException("WORD_RESOURCE_MISSING");
        var resourceId = Guid.Parse(reader.GetString(0)); var entryId = reader.GetString(1);
        var revision = reader.GetInt64(2); var epoch = reader.GetInt64(3);
        if (await reader.ReadAsync(token)) throw new InvalidDataException("WORD_RESOURCE_AMBIGUOUS");
        await reader.DisposeAsync();
        var now = DateTimeOffset.UtcNow;
        await ResourceStateStore.SetEntryRemovedInTransactionAsync(connection, transaction, resourceId, entryId,
            true, revision, now, new(Guid.NewGuid(), resourceId, ResourceOperationType.Remove, epoch, now, "{}"), token);
    }

    private static TextUnit Unit(TextUnitRole role, string text, TextUnit? previous, ContentDocument? source, CancellationToken token)
    {
        var seed = previous is null ? null : source! with
        {
            Title = text,
            TextUnits = [previous with { Role = TextUnitRole.Headword }]
        };
        var unit = DraftAnnotation.Generate(new TextDraft(text, text, ContentKind.Word)
            { Annotation = seed }, BundledAnnotationLexicon.Default.Engine, token).Document.TextUnits[0];
        return unit with { Role = role };
    }

    private static IReadOnlyDictionary<string, string> Translation(string english, string japanese)
    {
        var result = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(english)) result["en"] = english.Trim();
        if (!string.IsNullOrWhiteSpace(japanese)) result["ja"] = japanese.Trim();
        return result;
    }
}
