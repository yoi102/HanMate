namespace HanMate.App.Controls;

/// <summary>A quiet navigation surface backed by a native, keyboard-focusable button.</summary>
public sealed class NavigationTile : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(NavigationTile), "", propertyChanged: Changed);
    public static readonly BindableProperty DetailProperty = BindableProperty.Create(nameof(Detail), typeof(string), typeof(NavigationTile), "", propertyChanged: Changed);
    private readonly Label _title = new() { FontSize = 22, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _detail = new();
    private readonly Button _action = new();
    private readonly Grid _copy;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string? ActionId { get => _action.AutomationId; set => _action.AutomationId = value; }
    public object? CommandParameter { get; set; }
    public bool Compact
    {
        get => _title.FontSize == 16;
        set { _title.FontSize = value ? 16 : 22; _title.FontAttributes = value ? FontAttributes.None : FontAttributes.Bold;
            _copy.Padding = value ? new Thickness(16, 14) : new Thickness(20); _copy.MinimumHeightRequest = value ? 56 : 112; }
    }
    public event EventHandler? Clicked;

    public NavigationTile()
    {
        _detail.SetDynamicResource(StyleProperty, "LibraryMuted");
        _action.SetDynamicResource(StyleProperty, "LibraryTileAction");
        var words = new VerticalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { _title, _detail } };
        _copy = new Grid { Padding = 20, MinimumHeightRequest = 112, ColumnSpacing = 12,
            InputTransparent = true, CascadeInputTransparent = true,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var arrow = new Label { Text = "›", FontSize = 24, VerticalOptions = LayoutOptions.Center };
        arrow.SetDynamicResource(StyleProperty, "LibraryMuted");
        _copy.Add(words); _copy.Add(arrow, 1);
        AutomationProperties.SetIsInAccessibleTree(_title, false);
        AutomationProperties.SetIsInAccessibleTree(_detail, false);
        AutomationProperties.SetIsInAccessibleTree(arrow, false);
        var layers = new Grid(); layers.Add(_action); layers.Add(_copy);
        var surface = new Border { Content = layers }; surface.SetDynamicResource(StyleProperty, "LibrarySurface");
        Content = surface;
        _action.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        UpdateCopy();
    }
    private static void Changed(BindableObject sender, object oldValue, object newValue) => ((NavigationTile)sender).UpdateCopy();
    private void UpdateCopy()
    {
        _title.Text = Title; _detail.Text = Detail; _detail.IsVisible = !string.IsNullOrWhiteSpace(Detail);
        SemanticProperties.SetDescription(_action, Title);
        SemanticProperties.SetHint(_action, Detail);
    }
}
