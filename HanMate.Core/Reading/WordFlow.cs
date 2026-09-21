namespace HanMate.Core.Reading;

public readonly record struct WordSize(double Width, double Height);
public sealed record WordPlacement(int Index, double X, double Y, double Width, double Height);
public sealed record WordFlowResult(double Width, double Height, IReadOnlyList<WordPlacement> Placements);

/// <summary>Wrap whole platform-measured words; remeasure over-wide words before calculating row height.</summary>
public static class WordFlow
{
    public static WordFlowResult Arrange(int count, double width, Func<int, double, WordSize> measure, double spacing = 4)
    {
        if (count < 0 || double.IsNaN(width) || width < 0 || !double.IsFinite(spacing) || spacing < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (width == 0) return new(0, 0, []);
        var placements = new List<WordPlacement>(count);
        double x = 0, y = 0, rowHeight = 0, extent = 0;
        for (var i = 0; i < count; i++)
        {
            var natural = measure(i, double.PositiveInfinity);
            var itemWidth = Math.Min(width, Math.Ceiling(natural.Width));
            var measured = measure(i, itemWidth);
            if (!double.IsFinite(itemWidth) || itemWidth < 0 || !double.IsFinite(measured.Height) || measured.Height < 0)
                throw new ArgumentException("Invalid word measurement.", nameof(measure));
            if (x > 0 && x + spacing + itemWidth > width)
            { y += rowHeight + spacing; x = 0; rowHeight = 0; }
            else if (x > 0) x += spacing;
            var height = Math.Ceiling(measured.Height);
            placements.Add(new(i, x, y, itemWidth, height));
            x += itemWidth; rowHeight = Math.Max(rowHeight, height); extent = Math.Max(extent, x);
        }
        return new(extent, y + rowHeight, placements);
    }
}
