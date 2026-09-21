using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Core.Reading;

namespace HanMate.Infrastructure.Pinyin;

public sealed record LessonHeading(string Text, IReadOnlyList<RubyAtom> Atoms, string? Phonemes);

/// <summary>View-only metadata ruby; does not add title/author to saved body segments or recordings.</summary>
public static class LessonHeadingAnnotation
{
    private static readonly Lazy<IReadOnlyDictionary<string, PinyinSyllable[]>> Known = new(() =>
    {
        using var reader = new StreamReader(typeof(LessonHeadingAnnotation).Assembly
            .GetManifestResourceStream("Pinyin.LessonHeadingReadings.tsv")!);
        var result = new Dictionary<string, PinyinSyllable[]>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            result.Add(fields[0], fields[1].Split(' ').Select(PinyinSyllableParser.ParseDictionarySyllable).ToArray());
        }
        return result;
    });

    public static LessonHeading Create(string text, CancellationToken token = default)
    {
        var boundaries = TextElementMap.CreateUtf16Boundaries(text);
        var readings = new Dictionary<int, PinyinSyllable>();
        if (Known.Value.TryGetValue(text, out var known))
        {
            var index = 0;
            for (var i = 0; i < boundaries.Count - 1; i++)
                if (DeterministicPinyinCandidateEngine.IsHanziElement(TextElementMap.Slice(text, boundaries, i, 1)))
                    readings[i] = known[index++];
            if (index != known.Length) throw new InvalidDataException("Invalid lesson heading readings.");
        }
        else
        {
            foreach (var segment in BundledAnnotationLexicon.Default.Engine.Annotate(text, cancellationToken: token))
                if (segment.Selected is { } selected)
                    for (var i = 0; i < segment.Length; i++) readings[segment.Start + i] = selected.Syllables[i];
        }
        var atoms = new List<RubyAtom>();
        var phonemes = new List<string>(); var complete = true;
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            token.ThrowIfCancellationRequested();
            var element = TextElementMap.Slice(text, boundaries, i, 1);
            readings.TryGetValue(i, out var reading);
            var hanzi = DeterministicPinyinCandidateEngine.IsHanziElement(element);
            atoms.Add(new(Guid.Empty, Guid.Empty, null, i, 1, element, reading?.Display,
                element is "\r" or "\n" or "\r\n", element.Length == 1 && "）】》」』，。！？、".Contains(element),
                element.Length == 1 && "（【《「『".Contains(element), hanzi && reading is null));
            if (reading is { Erhua: false }) phonemes.Add(PinyinVoiceInput.Syllable(reading.Base, reading.Tone));
            else if (hanzi || element.EnumerateRunes().Any(System.Text.Rune.IsLetterOrDigit)) complete = false;
        }
        return new(text, atoms, complete && phonemes.Count is > 0 and <= 32 ? string.Join(' ', phonemes) : null);
    }
}
