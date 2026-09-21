using System.Text;

namespace HanMate.Core.Content;

/// <summary>Grammar-specific schema and role rules; never changes text, IDs or annotations.</summary>
public static class GrammarValidator
{
    public static ContentValidationResult Validate(ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<ContentValidationError>();
        if (document.Kind != ContentKind.Grammar)
        {
            if (document.Grammar is not null)
                Add("GRAMMAR_KIND_INVALID", "grammar", "Only grammar content may contain grammar data.");
            return new(errors);
        }

        var grammar = document.Grammar;
        if (grammar is null)
        {
            Add("GRAMMAR_REQUIRED", "grammar", "Grammar content requires structured grammar data.");
            return new(errors);
        }

        if (grammar.PatternParts is null || grammar.PatternParts.Count is < 1 or > 40)
            Add("GRAMMAR_PATTERN_INVALID", "grammar.patternParts", "Provide 1 through 40 pattern parts.");
        if (grammar.PatternParts is not null)
        {
            for (var index = 0; index < grammar.PatternParts.Count; index++)
            {
                var part = grammar.PatternParts[index];
                if (part is null || !Enum.IsDefined(part.Kind) || string.IsNullOrWhiteSpace(part.Text) || !ValidString(part.Text, 80))
                    Add("GRAMMAR_PATTERN_INVALID", $"grammar.patternParts[{index}]", "Pattern kind and nonblank text of at most 80 Unicode scalars are required.");
            }
        }

        if (grammar.PatternTranslations is null)
            Add("GRAMMAR_TRANSLATION_INVALID", "grammar.patternTranslations", "Provide a translation object, possibly empty.");
        else
            foreach (var pair in grammar.PatternTranslations)
                if (pair.Key is not ("ja" or "en") || pair.Value is null || !ValidString(pair.Value, 20_000))
                    Add("GRAMMAR_TRANSLATION_INVALID", $"grammar.patternTranslations.{pair.Key}", "Only valid Japanese and English auxiliary text is allowed.");

        if (grammar.TopicCodes is null || grammar.TopicCodes.Count > 20)
            Add("GRAMMAR_TOPIC_INVALID", "grammar.topicCodes", "Provide at most 20 topic codes.");
        if (grammar.TopicCodes is not null)
            foreach (var code in grammar.TopicCodes)
                if (string.IsNullOrEmpty(code) || code.Length > 64 || code[0] is < 'a' or > 'z' ||
                    code.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
                    Add("GRAMMAR_TOPIC_INVALID", "grammar.topicCodes", "Topic codes must match [a-z][a-z0-9-]{0,63}.");

        var units = new Dictionary<Guid, TextUnit>();
        if (document.TextUnits is null || document.TextUnits.Count > 41)
            Add("GRAMMAR_REFERENCE_INVALID", "textUnits", "Grammar requires a unit collection of at most 41 units.");
        if (document.TextUnits is not null)
            foreach (var unit in document.TextUnits)
            {
                if (unit is null || unit.Id == Guid.Empty || !units.TryAdd(unit.Id, unit))
                {
                    Add("GRAMMAR_REFERENCE_INVALID", "textUnits", "Grammar unit identities must be nonempty and unique.");
                    continue;
                }
                if (unit.Role is not (TextUnitRole.GrammarExplanation or TextUnitRole.Example or TextUnitRole.GrammarNote) ||
                    string.IsNullOrWhiteSpace(unit.Text) || !unit.Text.EnumerateRunes().Any(Rune.IsLetterOrDigit))
                    Add("GRAMMAR_REFERENCE_INVALID", "textUnits", "Grammar units need a grammar role and readable, nonblank text.");
            }

        var references = new HashSet<Guid>();
        Check(grammar.ExplanationUnitIds, TextUnitRole.GrammarExplanation, "explanationUnitIds", 1, 10);
        Check(grammar.ExampleUnitIds, TextUnitRole.Example, "exampleUnitIds", 1, 20);
        Check(grammar.NoteUnitIds, TextUnitRole.GrammarNote, "noteUnitIds", 0, 10);
        if (!references.SetEquals(units.Keys))
            Add("GRAMMAR_REFERENCE_INVALID", "grammar", "Every grammar unit must be referenced exactly once.");
        return new(errors);

        void Check(IReadOnlyList<Guid>? ids, TextUnitRole role, string field, int minimum, int maximum)
        {
            if (ids is null || ids.Count < minimum || ids.Count > maximum)
                Add("GRAMMAR_REFERENCE_INVALID", $"grammar.{field}", $"Provide {minimum} through {maximum} unit references.");
            if (ids is null) return;
            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index];
                if (!references.Add(id) || !units.TryGetValue(id, out var unit) || unit.Role != role)
                    Add("GRAMMAR_REFERENCE_INVALID", $"grammar.{field}[{index}]", "Reference is duplicate, missing, or has the wrong role.");
            }
        }

        void Add(string code, string path, string message) => errors.Add(new(code, path, message));
    }

    private static bool ValidString(string text, int maximumScalars)
    {
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index == text.Length || !char.IsLowSurrogate(text[index])) return false;
            }
            else if (char.IsLowSurrogate(text[index])) return false;
            if (++count > maximumScalars) return false;
        }
        return true;
    }
}
