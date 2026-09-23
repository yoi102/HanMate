using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Pinyin;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record LearningUnitInput(Guid Id, TextUnitRole Role, string Text, string English = "", string Japanese = "");
public sealed record PersonalLearningInput(ContentKind Kind, string Title, string Body, string Author,
    string Pattern, IReadOnlyList<LearningUnitInput> GrammarUnits, string English = "", string Japanese = "",
    string PatternEnglish = "", string PatternJapanese = "");

/// <summary>Edits personal lessons and grammar; bundled entries are copied and hidden in one transaction.</summary>
public sealed class PersonalLearningStore(HanMateDatabase database)
{
    public async Task<ContentDocument> SaveAsync(PersonalLearningInput input, ContentDocument? existing = null,
        CancellationToken token = default)
    {
        if (input.Kind is not (ContentKind.Text or ContentKind.Poem or ContentKind.Grammar) ||
            string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 120 || input.Author.Length > 120 ||
            input.English.Length > 20000 || input.Japanese.Length > 20000 ||
            input.PatternEnglish.Length > 20000 || input.PatternJapanese.Length > 20000 ||
            existing is not null && existing.Kind != input.Kind)
            throw new InvalidDataException("LEARNING_FIELDS_INVALID");
        var contentStore = new SqliteContentDocumentStore(database, new());
        ContentDocumentSnapshot? snapshot = null;
        if (existing is not null)
        {
            snapshot = await contentStore.GetAsync(existing.Id, token) ?? throw new InvalidDataException("LEARNING_MISSING");
            if (snapshot.Document.ContentRevision != existing.ContentRevision ||
                snapshot.Document.Origin is not (ContentOrigin.Resource or ContentOrigin.Personal))
                throw new HanMate.Core.Contracts.RevisionConflictException("content", existing.Id.ToString(), existing.ContentRevision);
        }
        var original = snapshot?.Document;
        var editable = original?.Origin == ContentOrigin.Resource ? EditorCommitStore.Copy(original) : original;
        var source = editable?.Source ?? new ContentSource
        {
            SourceId = "personal", Type = SourceType.Imported, AuthorProvider = "User", Reference = "User input",
            LicenseIdentifier = "NOASSERTION", PermissionNotes = "Local personal content.",
            CanDistribute = false, CanShare = false, BackupPolicy = BackupPolicy.Allowed, ReviewStatus = ReviewStatus.Draft
        };
        if (original?.Origin == ContentOrigin.Resource)
            source = source with { SourceId = "personal", Type = SourceType.Imported, ResourceId = null,
                ResourceVersion = null, EntryId = null, CanDistribute = false, CanShare = false,
                ReviewStatus = ReviewStatus.Draft };
        if (input.Kind == ContentKind.Poem) source = source with { AuthorProvider = input.Author.Trim() };
        ContentDocument document;
        if (input.Kind == ContentKind.Grammar)
        {
            if (input.GrammarUnits.Count == 0 || input.GrammarUnits.Any(u => u.Text.Length > 20000) ||
                !input.GrammarUnits.Any(u => u.Role == TextUnitRole.GrammarExplanation) ||
                !input.GrammarUnits.Any(u => u.Role == TextUnitRole.Example))
                throw new InvalidDataException("LEARNING_FIELDS_INVALID");
            if (editable is null)
            {
                var explanation = input.GrammarUnits.First(u => u.Role == TextUnitRole.GrammarExplanation).Text;
                var example = input.GrammarUnits.First(u => u.Role == TextUnitRole.Example).Text;
                editable = DraftAnnotation.Generate(new TextDraft(input.Title, explanation, ContentKind.Grammar)
                    { Pattern = input.Pattern, Example = example }, BundledAnnotationLexicon.Default.Engine, token).Document;
            }
            var originalIds = original?.TextUnits.Select(u => u.Id).ToArray() ?? [];
            var editableIds = editable.TextUnits.Select(u => u.Id).ToArray();
            var idMap = originalIds.Zip(editableIds).ToDictionary(p => p.First, p => p.Second);
            var units = input.GrammarUnits.Select(u => new GrammarUnitInput(idMap.GetValueOrDefault(u.Id, u.Id), u.Role,
                u.Text.Trim(), Translations(u.English, u.Japanese))).ToArray();
            var grammar = new GrammarEditInput(input.Title.Trim(), DraftAnnotation.ParsePattern(input.Pattern),
                Translations(input.PatternEnglish, input.PatternJapanese), editable.Grammar!.TopicCodes, units);
            document = GrammarEditing.Apply(editable, grammar, BundledAnnotationLexicon.Default.Engine, token).Document;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.Body)) throw new InvalidDataException("LEARNING_FIELDS_INVALID");
            var draft = new TextDraft(input.Title.Trim(), input.Body.Trim(), input.Kind, "textDraft.v2") { Annotation = editable };
            document = DraftAnnotation.Generate(draft, BundledAnnotationLexicon.Default.Engine, token).Document;
            var body = document.TextUnits.Single();
            document = document with { TextUnits = [body with { Translations = Translations(input.English, input.Japanese) }] };
        }
        document = document with { Source = source };
        await database.InitializeAsync(token);
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await contentStore.SaveInTransactionAsync(connection, transaction, document,
            original?.Origin == ContentOrigin.Personal ? snapshot!.RowRevision : 0, token);
        await EditorCommitStore.SyncTargetsAsync(connection, transaction, document, token);
        if (original?.Origin == ContentOrigin.Resource)
        {
            await PersonalWordStore.MoveFavoritesInTransactionAsync(connection, transaction, original.Id, document.Id, token);
            await PersonalWordStore.HideResourceInTransactionAsync(connection, transaction, original.Id, input.Kind, token);
        }
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
        return document;
    }

    public async Task HideResourceAsync(Guid contentId, ContentKind kind, CancellationToken token = default)
    {
        if (kind is not (ContentKind.Text or ContentKind.Poem or ContentKind.Grammar)) throw new ArgumentOutOfRangeException(nameof(kind));
        await database.InitializeAsync(token);
        await using var connection = await database.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await PersonalWordStore.HideResourceInTransactionAsync(connection, transaction, contentId, kind, token);
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static IReadOnlyDictionary<string, string> Translations(string english, string japanese)
    {
        var values = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(english)) values["en"] = english.Trim();
        if (!string.IsNullOrWhiteSpace(japanese)) values["ja"] = japanese.Trim();
        return values;
    }
}
