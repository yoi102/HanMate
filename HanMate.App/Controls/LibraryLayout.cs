namespace HanMate.App.Controls;

/// <summary>Shared list surfaces; data loading and actions stay in their owning pages.</summary>
internal static class LibraryLayout
{
    public static T Styled<T>(T view, string style) where T : VisualElement
    { view.SetDynamicResource(VisualElement.StyleProperty, style); return view; }

    public static Label Muted(string? text) => Styled(new Label { Text = text }, "LibraryMuted");

    public static Border Surface(View content, Thickness? padding = null) =>
        Styled(new Border { Content = content, Padding = padding ?? new Thickness(0) }, "LibrarySurface");

    public static Button Quiet(Button button) => Styled(button, "LibraryQuietButton");

    public static Grid Paging(Button previous, Button next, bool visible)
    {
        Quiet(previous); Quiet(next);
        var row = new Grid { IsVisible = visible, ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        row.Add(previous); row.Add(next, 1); return row;
    }

    // Keep errors measurable only while there is a message, including errors set by DataPage.RunAsync.
    public static Label Status()
    {
        var label = Muted(""); label.IsVisible = false;
        label.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Label.Text)) label.IsVisible = !string.IsNullOrWhiteSpace(label.Text); };
        return label;
    }

    public static Grid ContentRow(string titleBinding, Action<Button> configureOpen, Button? trailing = null, View? subtitle = null)
    {
        var open = Styled(new Button(), "LibraryTileAction"); open.SetBinding(SemanticProperties.DescriptionProperty, titleBinding);
        configureOpen(open);
        var title = new Label { FontSize = 18, LineBreakMode = LineBreakMode.WordWrap, FontAttributes = FontAttributes.Bold };
        title.SetBinding(Label.TextProperty, titleBinding); AutomationProperties.SetIsInAccessibleTree(title, false);
        var text = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(16, 16), InputTransparent = true, CascadeInputTransparent = true, VerticalOptions = LayoutOptions.Center };
        text.Add(title); if (subtitle is not null) text.Add(subtitle);
        var body = new Grid { MinimumHeightRequest = 64 }; body.Add(open); body.Add(text);
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; row.Add(body);
        if (trailing is not null) { trailing.VerticalOptions = LayoutOptions.Center; row.Add(trailing, 1); }
        return row;
    }
}
