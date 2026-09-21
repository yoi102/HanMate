using HanMate.Core.Reading;

namespace HanMate.Core.Tests;

public sealed class WordFlowTests
{
    [Fact]
    public void WholeWordsMoveToNextRowWithoutShrinking()
    {
        var result = WordFlow.Arrange(5, 156, (_, _) => new(74,48));
        Assert.Equal(new[] { 0d, 78, 0, 78, 0 }, result.Placements.Select(p => p.X));
        Assert.Equal(new[] { 0d, 0, 52, 52, 104 }, result.Placements.Select(p => p.Y));
        Assert.All(result.Placements, p => Assert.Equal(74, p.Width));
        Assert.Equal(152, result.Height);
    }
    [Theory]
    [InlineData(120,1)] [InlineData(240,1)] [InlineData(120,2)] [InlineData(240,2)]
    public void NarrowViewportAndLargeTypeKeepAllTextWithinRows(double width, double scale)
    {
        var lengths = new[] { 2,2,3,16,2 };
        var result = WordFlow.Arrange(lengths.Length, width, (i,constraint) =>
        {
            var natural = lengths[i] * 18 * scale + 18;
            var capacity = double.IsPositiveInfinity(constraint) ? lengths[i] : Math.Max(1,Math.Floor((constraint-18)/(18*scale)));
            return new(Math.Min(natural,constraint),Math.Max(48,Math.Ceiling(lengths[i]/capacity)*24*scale+6));
        });
        Assert.Equal(lengths.Length,result.Placements.Count);
        foreach(var p in result.Placements)
        {
            Assert.True(p.X>=0 && p.X+p.Width<=width);
            Assert.True(p.Y+p.Height<=result.Height);
        }
        foreach(var row in result.Placements.GroupBy(p=>p.Y))
        {
            var next = result.Placements.Where(p=>p.Y>row.Key).Select(p=>p.Y).DefaultIfEmpty(result.Height).Min();
            Assert.True(row.Max(p=>p.Y+p.Height)<=next);
        }
        Assert.True(result.Placements[3].Height>48);
    }
    [Fact]
    public void OverWideWordIsRemeasuredAtActualWidthBeforeRowHeightIsChosen()
    {
        var result = WordFlow.Arrange(2,100,(i,width)=>i==0 ? new(300, width<300 ? 96 : 32) : new(40,48));
        Assert.Equal(new WordPlacement(0,0,0,100,96),result.Placements[0]);
        Assert.Equal(100,result.Placements[1].Y); Assert.Equal(148,result.Height);
    }
    [Fact]
    public void FractionalFontMetricsAreRoundedUpAndEmptyLayoutHasNoHeight()
    {
        var result = WordFlow.Arrange(1,100,(_,_)=>new(59.1,48.2));
        Assert.Equal(60,result.Placements[0].Width); Assert.Equal(49,result.Height);
        Assert.Empty(WordFlow.Arrange(0,100,(_,_)=>throw new Exception()).Placements);
        Assert.Equal(0,WordFlow.Arrange(0,100,(_,_)=>throw new Exception()).Height);
    }
}
