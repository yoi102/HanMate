using System.Globalization;
using System.Text;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Content;

public sealed record GrammarUnitInput(Guid Id, TextUnitRole Role, string Text, IReadOnlyDictionary<string, string> Translations);
public sealed record GrammarEditInput(string Title, IReadOnlyList<GrammarPatternPart> Parts,
    IReadOnlyDictionary<string, string> Translations, IReadOnlyList<string> Topics, IReadOnlyList<GrammarUnitInput> Units);

/// <summary>Edits by stable unit identity, never by role or position. Untouched units keep their exact annotation graph.</summary>
public static class GrammarEditing
{
    public static GrammarEditInput From(ContentDocument document)
    {
        var grammar = document.Grammar ?? throw new InvalidDataException("GRAMMAR_REQUIRED");
        var units = document.TextUnits.ToDictionary(u => u.Id);
        return new(document.Title, grammar.PatternParts, grammar.PatternTranslations, grammar.TopicCodes,
            grammar.ExplanationUnitIds.Concat(grammar.ExampleUnitIds).Concat(grammar.NoteUnitIds)
                .Select(id => new GrammarUnitInput(id, units[id].Role, units[id].Text, units[id].Translations)).ToArray());
    }

    public static AnnotationPreview Apply(ContentDocument original, GrammarEditInput input,
        DeterministicPinyinCandidateEngine engine, CancellationToken token = default)
    {
        if (original.Kind != ContentKind.Grammar || original.Origin != ContentOrigin.Personal) throw new InvalidOperationException("GRAMMAR_PERSONAL_REQUIRED");
        var utf8 = new UTF8Encoding(false, true);
        _ = utf8.GetByteCount(input.Title);
        foreach (var unit in input.Units)
        {
            _ = utf8.GetByteCount(unit.Text);
            foreach (var pair in unit.Translations)
            {
                _ = utf8.GetByteCount(pair.Value);
                if (pair.Key is not ("ja" or "en") || pair.Value.EnumerateRunes().Count() > 20000) throw new InvalidDataException("GRAMMAR_TRANSLATION_INVALID");
            }
        }
        if (string.IsNullOrWhiteSpace(input.Title) || new StringInfo(input.Title).LengthInTextElements > 120 ||
            input.Units.Count > 40 || input.Units.Select(u => u.Id).Distinct().Count() != input.Units.Count || input.Units.Any(u => u.Id == Guid.Empty) ||
            input.Units.Sum(u => (long)new StringInfo(u.Text).LengthInTextElements) > 20000 ||
            input.Units.Sum(u => (long)Encoding.UTF8.GetByteCount(u.Text)) > TextDraftInput.MaxBytes) throw new InvalidDataException("GRAMMAR_INPUT_LIMIT");
        var previous = original.TextUnits.ToDictionary(u => u.Id);
        var units = new List<TextUnit>();
        foreach (var inputUnit in input.Units)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(inputUnit.Text)) throw new InvalidDataException("GRAMMAR_REQUIRED");
            previous.TryGetValue(inputUnit.Id, out var old);
            if (old is not null && old.Role != inputUnit.Role) throw new InvalidDataException("GRAMMAR_ROLE_CHANGED");
            TextUnit unit;
            if (old?.Text == inputUnit.Text) unit = old;
            else
            {
                // Reuse the bounded annotation algorithm in a single-unit document. Other units never participate in matching.
                var draft = new TextDraft(input.Title, inputUnit.Text, ContentKind.Text, "textDraft.v2")
                {
                    Annotation = old is null ? null : original with { Kind = ContentKind.Text, Grammar = null, TextUnits = [old with { Role = TextUnitRole.Body }] }
                };
                unit = DraftAnnotation.Generate(draft, engine, token).Document.TextUnits.Single() with { Id = inputUnit.Id, Role = inputUnit.Role };
            }
            units.Add(unit with { Translations = new Dictionary<string, string>(inputUnit.Translations) });
        }
        var document = original with
        {
            Title = input.Title, TextUnits = units, ContentRevision = original.ContentRevision + 1,
            AnnotationRevision = original.AnnotationRevision + 1, MetadataRevision = original.MetadataRevision + 1, UpdatedAtUtc = DateTimeOffset.UtcNow,
            Grammar = new()
            {
                PatternParts = input.Parts.ToArray(), PatternTranslations = new Dictionary<string, string>(input.Translations), TopicCodes = input.Topics.Distinct().ToArray(),
                ExplanationUnitIds = Ids(TextUnitRole.GrammarExplanation), ExampleUnitIds = Ids(TextUnitRole.Example), NoteUnitIds = Ids(TextUnitRole.GrammarNote)
            }
        };
        var validation = new ContentDocumentValidator().Validate(document);
        if (!validation.IsValid) throw new InvalidDataException(validation.Errors[0].Code);
        var retained = units.SelectMany(u => u.Tokens).Select(t => t.Id).ToHashSet();
        return new(document, original.TextUnits.SelectMany(u => u.Tokens).Count(t => t.Locked && !retained.Contains(t.Id)));
        Guid[] Ids(TextUnitRole role) => units.Where(u => u.Role == role).Select(u => u.Id).ToArray();
    }
}
