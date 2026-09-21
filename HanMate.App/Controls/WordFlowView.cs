using HanMate.Core.Reading;
using Microsoft.Maui.Layouts;

namespace HanMate.App.Controls;

/// <summary>Natural word widths with bounded multiline measurement, without flex shrink.</summary>
public sealed class WordFlowView : Layout
{
    protected override ILayoutManager CreateLayoutManager() => new Manager(this);
    private sealed class Manager(WordFlowView view) : ILayoutManager
    {
        private WordFlowResult Compute(double width) => WordFlow.Arrange(view.Count, Math.Max(0, width), (index, constraint) =>
        {
            var size = view[index].Measure(constraint, double.PositiveInfinity);
            return new(size.Width, size.Height);
        });
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var result = Compute(widthConstraint);
            return new(result.Width, result.Height);
        }
        public Size ArrangeChildren(Rect bounds)
        {
            var result = Compute(bounds.Width);
            foreach (var item in result.Placements)
                view[item.Index].Arrange(new(bounds.X + item.X, bounds.Y + item.Y, item.Width, item.Height));
            return new(bounds.Width, result.Height);
        }
    }
}
