using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Infrastructure.Pinyin;

/// <summary>Separate from installable search dictionaries; no application network access.</summary>
public sealed class BundledAnnotationLexicon
{
    public const string Version = "Unicode-17.0.0-kMandarin-kHanyuPinyin-v1+context-v1";
    private static readonly Lazy<BundledAnnotationLexicon> Shared = new(() => new());
    public static BundledAnnotationLexicon Default => Shared.Value;
    public DeterministicPinyinCandidateEngine Engine { get; }
    public int AcceptedReadings { get; }
    public int AcceptedCharacters { get; }
    public IReadOnlyList<string> UnsupportedReadings { get; }
    private BundledAnnotationLexicon()
    {
        using var stream = typeof(BundledAnnotationLexicon).Assembly.GetManifestResourceStream("Pinyin.Unihan17.tsv")
            ?? throw new InvalidDataException("ANNOTATION_LEXICON_MISSING");
        using var reader = new StreamReader(stream);
        var entries = new List<PinyinLexiconEntry>(); var unsupported = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var columns = line.Split('\t');
            if (columns.Length != 2) throw new InvalidDataException("ANNOTATION_LEXICON_INVALID");
            if (!DeterministicPinyinCandidateEngine.IsHanziElement(columns[0]) || !PinyinSyllableParser.TryParseDictionarySyllable(columns[1], out var syllable))
            { unsupported.Add(line); continue; }
            entries.Add(new() { Text = columns[0], Syllables = [syllable], Tier = PinyinLexiconTier.CharacterDictionary, Priority = 0, SourceId = Version });
        }
        AcceptedReadings = entries.Count; AcceptedCharacters = entries.Select(e => e.Text).Distinct(StringComparer.Ordinal).Count(); UnsupportedReadings = unsupported.AsReadOnly();
        using var phrases = new StreamReader(typeof(BundledAnnotationLexicon).Assembly.GetManifestResourceStream("Pinyin.ContextSeed.tsv")!);
        while (phrases.ReadLine() is { } phrase)
        {
            var columns = phrase.Split('\t');
            entries.Add(new() { Text = columns[0], Syllables = columns[1].Split(' ').Select(PinyinSyllableParser.ParseDictionarySyllable).ToArray(),
                Tier = PinyinLexiconTier.PhraseDictionary, Priority = 0, SourceId = "HanMate-context-seed-v1" });
        }
        Engine = new(entries);
    }
}
