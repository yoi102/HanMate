namespace HanMate.Core.Reading;

public sealed record RubyMeasure(RubyAtom Atom, double Width, double Height);
public sealed record RubyPlacement(int Index, double X, double Y, double Width, double Height);
public sealed record RubyLayoutResult(double Height, IReadOnlyList<RubyPlacement> Placements);

/// <summary>Pure wrapping of platform-measured cells. Never splits a ruby pair or a grapheme.</summary>
public static class RubyLineLayout
{
    public static RubyLayoutResult Arrange(IReadOnlyList<RubyMeasure> cells, double width, double emptyLineHeight)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(emptyLineHeight) || emptyLineHeight <= 0 ||
            cells.Any(c => !double.IsFinite(c.Width) || c.Width < 0 || !double.IsFinite(c.Height) || c.Height < 0))
            throw new ArgumentOutOfRangeException(nameof(width));
        var placements = new List<RubyPlacement>(cells.Count); var y = 0d; var start = 0;
        while (start < cells.Count)
        {
            if (cells[start].Atom.HardBreak)
            {
                placements.Add(new(start, 0, y, 0, emptyLineHeight)); y += emptyLineHeight; start++; continue;
            }
            var end = start; var occupied = 0d;
            while (end < cells.Count && !cells[end].Atom.HardBreak && (end == start || occupied + cells[end].Width <= width))
            { occupied += cells[end].Width; end++; }
            if (end < cells.Count && !cells[end].Atom.HardBreak)
            {
                var candidate = end;
                while (candidate > start && !ReadingDocument.CanBreak(cells[candidate - 1].Atom, cells[candidate].Atom)) candidate--;
                if (candidate > start) end = candidate;
                // An over-wide unannotated word can break between graphemes; a single ruby cell stays intact.
            }
            var height = Math.Max(emptyLineHeight, cells.Skip(start).Take(end - start).Max(c => c.Height));
            var x = 0d;
            for (var i = start; i < end; i++)
            { var c = cells[i]; placements.Add(new(i, x, y + height - c.Height, c.Width, c.Height)); x += c.Width; }
            y += height; start = end;
            if (start < cells.Count && cells[start].Atom.HardBreak)
            {
                placements.Add(new(start, x, y - height, 0, height)); start++;
            }
        }
        if (cells.Count > 0 && cells[^1].Atom.HardBreak) y += emptyLineHeight;
        return new(y, placements);
    }
}
