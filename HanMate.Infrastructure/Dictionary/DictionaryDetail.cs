using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Dictionary;

public sealed record DictionaryText(string Text, IReadOnlyList<RubyAtom> Atoms, bool Supplement = false);
public sealed record DictionaryDetail(DictionaryText Headword, IReadOnlyList<DictionaryText> Definitions,
    IReadOnlyList<DictionaryText> Examples, string Notes, bool HasAutomaticPinyin);

public static class DictionaryDetailProjection
{
    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> Samples = new(() =>
    {
        using var reader = new StreamReader(typeof(DictionaryDetailProjection).Assembly.GetManifestResourceStream("Dictionary.learning-examples.tsv")!);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).Select(l => l.TrimEnd('\r').Split('\t'))
            .GroupBy(p => p[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p[1]).ToArray(), StringComparer.Ordinal);
    });

    public static DictionaryDetail Create(ContentDocument document, CancellationToken token = default, bool sourceOnly = false)
    {
        var reading = new ReadingDocument(document);
        var atoms = reading.Pages.SelectMany(p => p.Parts).GroupBy(p => p.Unit.Id).ToDictionary(g => g.Key, g => g.SelectMany(p => p.Atoms).ToArray());
        var head = document.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
        if (sourceOnly)
        {
            // Learning meanings use exactly the saved units: no dictionary lookup, added examples or replacement readings.
            token.ThrowIfCancellationRequested();
            DictionaryText Saved(TextUnit unit) => new(unit.Text, atoms[unit.Id]);
            return new(Saved(head), document.TextUnits.Where(u => u.Role == TextUnitRole.Definition).Select(Saved).ToArray(),
                document.TextUnits.Where(u => u.Role == TextUnitRole.Example).Select(Saved).ToArray(), "", false);
        }
        var automatic = false;
        DictionaryText Annotate(string text, IReadOnlyList<RubyAtom> original, bool supplement = false)
        {
            var generated = DictionaryDisplayPinyin.Annotate(text, token);
            return new(text, original.Select(a =>
            {
                if (a.Pinyin is not null || a.Length != 1 || !a.MissingPinyin || !generated.TryGetValue(a.Start, out var pinyin)) return a;
                automatic = true; return a with { Pinyin = pinyin, MissingPinyin = false };
            }).ToArray(), supplement);
        }
        DictionaryText Sentence(string text, bool supplement)
        {
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(document.Id + ":example:" + text)).AsSpan(0, 16));
            // Create a view-only unit to reuse Unicode-safe ruby projection; never saved into the dictionary.
            var sample = DefaultDictionaryStore.Project(id, new(text, "", true, ["示例"], [], []));
            var unit = sample.TextUnits[0];
            var source = new ReadingDocument(sample).Pages.SelectMany(p => p.Parts).Where(p => p.Unit.Id == unit.Id).SelectMany(p => p.Atoms).ToArray();
            return Annotate(text, source, supplement);
        }
        var metadata = document.Source.SourceId == DefaultDictionaryStore.SourceId ? DefaultDictionaryStore.MetadataUnitId(document.Id) : Guid.Empty;
        var definitions = document.TextUnits.Where(u => u.Role == TextUnitRole.Definition && u.Id != metadata)
            .Select(u => Annotate(u.Text, atoms[u.Id])).ToArray();
        var examples = new List<DictionaryText>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in document.TextUnits.Where(u => u.Role == TextUnitRole.Example))
        {
            token.ThrowIfCancellationRequested();
            var text = unit.Text.Replace("～", head.Text, StringComparison.Ordinal);
            if (seen.Add(text)) examples.Add(text == unit.Text ? Annotate(text, atoms[unit.Id]) : Sentence(text, false));
        }
        // Only copy explicitly introduced source usages. Keep the full original definition as well.
        foreach (var definition in definitions)
            foreach (Match match in Regex.Matches(definition.Text, @"(?:例如|如)[：:∶]([^。\r\n]{2,100}[。]?)"))
            {
                var text = match.Groups[1].Value.Trim().Replace("～", head.Text, StringComparison.Ordinal);
                if (text.Contains(head.Text, StringComparison.Ordinal) && seen.Add(text)) examples.Add(Sentence(text, false));
                if (examples.Count >= 6) break;
            }
        if (examples.Count < 2 && Samples.Value.TryGetValue(head.Text, out var supplements))
            foreach (var text in supplements)
                if (seen.Add(text)) examples.Add(Sentence(text, true));
        var header = Annotate(head.Text, atoms[head.Id]);
        return new(header, definitions, examples, document.TextUnits.FirstOrDefault(u => u.Id == metadata)?.Text ?? "", automatic);
    }

    public static IReadOnlySet<int> HighlightStarts(string text, string headword)
    {
        var bounds = TextElementMap.CreateUtf16Boundaries(text); var starts = new HashSet<int>();
        if (string.IsNullOrEmpty(headword)) return starts;
        for (var offset = 0; offset < text.Length;)
        {
            var index = text.IndexOf(headword, offset, StringComparison.Ordinal); if (index < 0) break;
            var end = index + headword.Length;
            if (bounds.Contains(index) && bounds.Contains(end))
                for (var i = 0; i < bounds.Count - 1; i++) if (bounds[i] >= index && bounds[i] < end) starts.Add(i);
            offset = end;
        }
        return starts;
    }
}
