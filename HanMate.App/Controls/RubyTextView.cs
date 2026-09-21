using HanMate.Core.Reading;
using Microsoft.Maui.Layouts;

namespace HanMate.App.Controls;

/// <summary>One bounded visual page of native accessible text. Domain content stays in ReadingDocument.</summary>
public sealed class RubyTextView : Layout
{
    private readonly IReadOnlyList<RubyAtom> _atoms;
    private readonly double _minimumLineHeight;
    public RubyTextView(IReadOnlyList<RubyAtom> atoms, bool showPinyin, double scale, Action<RubyAtom>? selected,
        IReadOnlySet<int>? highlights = null, bool dictionaryStyle = false)
    {
        if (atoms.Count > ReadingDocument.PageAtomLimit) throw new ArgumentException("Paginate the reading projection first.", nameof(atoms));
        _atoms = atoms; _minimumLineHeight = (showPinyin ? 50 : 34) * scale;
        // Reading exposes the visual unit once. The explicit enlarge/next controls provide accessible navigation.
        SemanticProperties.SetDescription(this, string.Concat(atoms.Select(a => a.Text)));
        foreach (var atom in atoms)
        {
            if (atom.HardBreak) { Children.Add(new BoxView { WidthRequest = 0, HeightRequest = 0, InputTransparent = true }); continue; }
            var cell = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(atom.Pinyin is null ? 0 : 2, 2) };
            var highlighted = highlights?.Contains(atom.Start) == true;
            if (showPinyin)
            {
                var ruby = new Label { Text = atom.Pinyin ?? (atom.MissingPinyin && !dictionaryStyle ? "?" : "\u00a0"), FontSize = 15 * scale,
                    LineBreakMode = LineBreakMode.NoWrap, HorizontalTextAlignment = TextAlignment.Center };
                if (highlighted) ruby.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#B3261E"), Color.FromArgb("#FFA8A1"));
                else if (dictionaryStyle) ruby.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#73717A"), Color.FromArgb("#BBB7C3"));
                cell.Add(ruby);
            }
            var text = new Label { Text = atom.Text, FontSize = 26 * scale, LineBreakMode = LineBreakMode.NoWrap, HorizontalTextAlignment = TextAlignment.Center,
                FontAttributes = highlighted ? FontAttributes.Bold : FontAttributes.None,
                MinimumWidthRequest = string.IsNullOrWhiteSpace(atom.Text) ? 7 * scale : 0 };
            if (highlighted) text.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#B3261E"), Color.FromArgb("#FFA8A1"));
            else if (dictionaryStyle) text.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#26232B"), Color.FromArgb("#F2EFF5"));
            cell.Add(text);
            cell.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
            if (selected is not null && atom.SegmentId is not null)
            {
                var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => selected(atom); cell.GestureRecognizers.Add(tap);
            }
            Children.Add(cell);
        }
    }

    public void SetPlayingTarget(Guid? target)
    {
        for (var i = 0; i < _atoms.Count; i++)
            if (Children[i] is VisualElement element)
                element.BackgroundColor = target is not null && (_atoms[i].UnitId == target || _atoms[i].SegmentId == target)
                    ? Color.FromArgb("#E6DEF7") : Colors.Transparent;
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);
    private sealed class Manager(RubyTextView view) : ILayoutManager
    {
        private RubyLayoutResult Compute(double width)
        {
            var measures = new List<RubyMeasure>(view.Count);
            for (var i = 0; i < view.Count; i++)
            {
                var size = view[i].Measure(double.PositiveInfinity, double.PositiveInfinity);
                measures.Add(new(view._atoms[i], size.Width, size.Height));
            }
            return RubyLineLayout.Arrange(measures, Math.Max(1, double.IsFinite(width) ? width : 600), view._minimumLineHeight);
        }
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var result = Compute(widthConstraint);
            var extent = result.Placements.Count == 0 ? 0 : result.Placements.Max(p => p.X + p.Width);
            return new(Math.Min(double.IsFinite(widthConstraint) ? widthConstraint : extent, extent), result.Height);
        }
        public Size ArrangeChildren(Rect bounds)
        {
            var result = Compute(bounds.Width);
            foreach (var cell in result.Placements) view[cell.Index].Arrange(new(bounds.X + cell.X, bounds.Y + cell.Y, cell.Width, cell.Height));
            return new(bounds.Width, result.Height);
        }
    }
}
