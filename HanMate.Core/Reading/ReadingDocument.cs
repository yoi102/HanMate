using HanMate.Core.Content;

namespace HanMate.Core.Reading;

public sealed record ReadingTarget(Guid UnitId, Guid? SegmentId);
public sealed record RubyAtom(Guid UnitId, Guid TokenId, Guid? SegmentId, int Start, int Length,
    string Text, string? Pinyin, bool HardBreak, bool JoinPrevious, bool JoinNext, bool MissingPinyin = false);
public sealed record ReadingPart(TextUnit Unit, IReadOnlyList<RubyAtom> Atoms, bool StartsUnit, bool EndsUnit);
public sealed record ReadingPage(IReadOnlyList<ReadingPart> Parts);

/// <summary>Immutable view projection. Visual pages never change a saved token or speech segment.</summary>
public sealed class ReadingDocument
{
    public const int PageAtomLimit = 96;
    public ContentDocument Content { get; }
    public IReadOnlyList<ReadingTarget> Targets { get; }
    public IReadOnlyList<ReadingPage> Pages { get; }
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<RubyAtom>> _atoms;
    private readonly IReadOnlyDictionary<Guid, TextUnit> _units;

    public ReadingDocument(ContentDocument content)
    {
        if (!new ContentDocumentValidator().Validate(content).IsValid) throw new InvalidDataException("Invalid reading document.");
        Content = content;
        _units = content.TextUnits.ToDictionary(u => u.Id);
        var ordered = content.Grammar is { } grammar
            ? grammar.ExplanationUnitIds.Concat(grammar.ExampleUnitIds).Concat(grammar.NoteUnitIds).Select(id => _units[id]).ToArray()
            : content.TextUnits;
        _atoms = ordered.ToDictionary(u => u.Id, Project);
        Targets = ordered.SelectMany(u => content.Kind is ContentKind.Text or ContentKind.Poem
            ? u.Segments.Where(s => s.Kind == SegmentKind.Speech).Select(s => new ReadingTarget(u.Id, s.Id))
            : new[] { new ReadingTarget(u.Id, null) }).ToArray();
        Pages = Paginate(ordered.Select(u => (u, _atoms[u.Id])));
    }

    public TextUnit Unit(ReadingTarget target) => _units[target.UnitId];
    public TextSegment? Segment(ReadingTarget target) => target.SegmentId is { } id ? Unit(target).Segments.Single(s => s.Id == id) : null;
    public IReadOnlyList<ReadingPage> PagesFor(ReadingTarget target)
    {
        if (!Targets.Contains(target)) throw new ArgumentException("Unknown reading target.", nameof(target));
        var unit = Unit(target);
        var atoms = target.SegmentId is { } id ? _atoms[unit.Id].Where(a => a.SegmentId == id).ToArray() : _atoms[unit.Id];
        return Paginate(new[] { (unit, atoms) });
    }

    public ReadingTarget? TargetFor(RubyAtom atom) => Content.Kind is ContentKind.Text or ContentKind.Poem
        ? atom.SegmentId is { } id ? new(atom.UnitId, id) : null
        : new(atom.UnitId, null);

    private static IReadOnlyList<RubyAtom> Project(TextUnit unit)
    {
        var result = new List<RubyAtom>();
        var segments = unit.Segments.SelectMany(s => s.TokenIds.Select(id => (id, segment: s.Kind == SegmentKind.Speech ? (Guid?)s.Id : null))).ToDictionary(x => x.id, x => x.segment);
        foreach (var token in unit.Tokens)
        {
            var segmentId = segments[token.Id];
            if (token.Pinyin is { } pinyin)
            {
                result.Add(new(unit.Id, token.Id, segmentId, token.Start, token.Length, token.Text, pinyin.Display, false, false, false));
                continue;
            }
            var bounds = TextElementMap.CreateUtf16Boundaries(token.Text);
            for (var index = 0; index < bounds.Count - 1; index++)
            {
                var text = TextElementMap.Slice(token.Text, bounds, index, 1);
                var lineBreak = text is "\r\n" or "\r" or "\n" or "\u2028" or "\u2029";
                var previousIsWord = index > 0 && IsWord(TextElementMap.Slice(token.Text, bounds, index - 1, 1));
                result.Add(new(unit.Id, token.Id, segmentId, token.Start + index, 1, text, null, lineBreak,
                    !lineBreak && (IsClosing(text) || (IsWord(text) && previousIsWord)), !lineBreak && IsOpening(text), token.Kind == TokenKind.Hanzi));
            }
        }
        return result;
    }

    private static bool IsWord(string text) => text.Length == 1 && (char.IsAsciiLetterOrDigit(text[0]) || text[0] is '_' or '-' or '.' or ':' or '/' or '@');
    private static bool IsClosing(string text) => text.Length == 1 && "，。！？；：、,.!?;:…）】》」』”’)]}".Contains(text[0]);
    private static bool IsOpening(string text) => text.Length == 1 && "（【《「『“‘([{".Contains(text[0]);
    public static bool CanBreak(RubyAtom left, RubyAtom right) => left.HardBreak || right.HardBreak || (!left.JoinNext && !right.JoinPrevious);

    private static IReadOnlyList<ReadingPage> Paginate(IEnumerable<(TextUnit Unit, IReadOnlyList<RubyAtom> Atoms)> units)
    {
        var pages = new List<ReadingPage>();
        var parts = new List<ReadingPart>();
        var used = 0;
        foreach (var (unit, atoms) in units)
        {
            for (var start = 0; start < atoms.Count;)
            {
                var end = Math.Min(atoms.Count, start + PageAtomLimit - used);
                if (end < atoms.Count)
                {
                    var candidate = end;
                    while (candidate > start && !CanBreak(atoms[candidate - 1], atoms[candidate])) candidate--;
                    if (candidate > start) end = candidate;
                }
                var take = atoms.Skip(start).Take(end - start).ToArray();
                parts.Add(new(unit, take, start == 0, end == atoms.Count)); used += take.Length; start = end;
                if (start < atoms.Count || used == PageAtomLimit) Flush();
            }
        }
        if (parts.Count > 0) Flush();
        return pages;

        void Flush() { pages.Add(new(parts.ToArray())); parts.Clear(); used = 0; }
    }
}
