using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Infrastructure.Pinyin;

/// <summary>Automatic reading aids for dictionary display, isolated from editable annotations.</summary>
public static class DictionaryDisplayPinyin
{
    private static readonly Lazy<DeterministicPinyinCandidateEngine> Engine = new(Load);
    public static void Prepare() => _ = Engine.Value;
    private static DeterministicPinyinCandidateEngine Load()
    {
        using var reader = new StreamReader(typeof(DictionaryDisplayPinyin).Assembly.GetManifestResourceStream("Pinyin.DictionaryDisplay.tsv")!);
        var entries = new List<PinyinLexiconEntry>();
        // The 89k phrases reuse a small set of syllables. Parse each spelling once at cold load.
        var readings = new Dictionary<string, PinyinSyllable?>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t'); var values = parts[1].Split(' ');
            var bounds = TextElementMap.CreateUtf16Boundaries(parts[0]);
            var syllables = new List<PinyinSyllable>();
            if (bounds.Count - 1 != values.Length) continue;
            for (var i = 0; i < values.Length; i++)
            {
                if (!readings.TryGetValue(values[i], out var reading))
                {
                    reading = PinyinSyllableParser.TryParseDictionarySyllable(values[i], out var parsed) ? parsed : null;
                    readings.Add(values[i], reading);
                }
                if (!DeterministicPinyinCandidateEngine.IsHanziElement(TextElementMap.Slice(parts[0], bounds, i, 1)) || reading is null) break;
                syllables.Add(reading);
            }
            if (syllables.Count != values.Length) continue;
            entries.Add(new() { Text = parts[0], Syllables = syllables, Priority = 1,
                Tier = syllables.Count == 1 ? PinyinLexiconTier.CharacterDictionary : PinyinLexiconTier.PhraseDictionary,
                SourceId = "pypinyin-0.55.0-display" });
        }
        using var overrides = new StreamReader(typeof(DictionaryDisplayPinyin).Assembly.GetManifestResourceStream("Pinyin.DictionaryDisplayOverrides.tsv")!);
        while (overrides.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            entries.Add(new() { Text = parts[0], Syllables = parts[1].Split(' ').Select(PinyinSyllableParser.ParseDictionarySyllable).ToArray(),
                Priority = 0, Tier = PinyinLexiconTier.PhraseDictionary, SourceId = "HanMate-display-context-draft" });
        }
        return new(entries);
    }

    public static IReadOnlyDictionary<int, string> Annotate(string text, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = new Dictionary<int, string>();
        foreach (var segment in Engine.Value.Annotate(text, cancellationToken: token))
            if (segment.Selected is { } selected)
                for (var i = 0; i < selected.Syllables.Count; i++) result[segment.Start + i] = selected.Syllables[i].Display;
        return result;
    }
}
