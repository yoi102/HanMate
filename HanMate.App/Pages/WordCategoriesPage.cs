using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class WordCategoriesPage(LearningCatalogStore store, LocalizationService language) : DataPage(language)
{
    private WordCategoryCounts? _renderedCounts;
    private string? _renderedCulture;
    private readonly Dictionary<string, LearningBrowserPage> _browsers = [];
    protected override async Task RefreshAsync()
    {
        if (_renderedCounts is not null && _renderedCulture == Language.CurrentCultureName)
        {
            var counts = await Task.Run(() => store.GetWordCategoryCountsAsync());
            if (counts.Total == _renderedCounts.Total && counts.Counts.Count == _renderedCounts.Counts.Count &&
                counts.Counts.All(p => _renderedCounts.Counts.TryGetValue(p.Key, out var value) && value == p.Value)) return;
        }
        await ReloadAsync();
    }
    protected override async Task ReloadAsync()
    {
        Title = Language.KindWord;
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        Status = LibraryLayout.Status();
        var counts = await Task.Run(() => store.GetWordCategoryCountsAsync());
        var body = new VerticalStackLayout { Padding = 20, Spacing = 16, MaximumWidthRequest = 920 };
        body.Add(new Label { Text = Language["WordCategories.Title"], FontSize = 24, FontAttributes = FontAttributes.Bold });
        body.Add(LibraryLayout.Muted(Language["WordCategories.Hint"]));
        body.Add(Tile("all", counts.Total, compact: true));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var category in WordCategories.All)
            grid.Children.Add(Tile(category, counts.Counts.GetValueOrDefault(category)));
        if (counts.Counts.GetValueOrDefault(WordCategories.Other) > 0)
            grid.Children.Add(Tile(WordCategories.Other, counts.Counts[WordCategories.Other]));
        var columns = 0;
        void Reflow()
        {
            var next = grid.Width >= 640 ? 3 : grid.Width >= 320 ? 2 : 1;
            if (next == columns) return;
            columns = next;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new(GridLength.Star));
            for (var i = 0; i < (grid.Children.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new(GridLength.Auto));
            for (var i = 0; i < grid.Children.Count; i++)
            { grid.SetRow(grid.Children[i], i / columns); grid.SetColumn(grid.Children[i], i % columns); }
        }
        grid.SizeChanged += (_, _) => Reflow();
        Reflow();
        body.Add(grid); body.Add(Status);
        Content = new ScrollView { AutomationId = "WordCategories.Scroll", Content = body };
        _renderedCounts = counts; _renderedCulture = Language.CurrentCultureName;
    }

    private NavigationTile Tile(string category, int count, bool compact = false)
    {
        var tile = new NavigationTile
        {
            Title = Language["WordCategories." + category], Compact = compact,
            Detail = string.Format(Language["WordCategories.Count"], count),
            ActionId = "WordCategories." + category
        };
        tile.Clicked += async (_, _) => await RunAsync(() =>
        {
            if (!_browsers.TryGetValue(category, out var page))
                _browsers[category] = page = new LearningBrowserPage(store, Language, ContentKind.Word, category == "all" ? null : category);
            return Navigation.PushAsync(page);
        });
        return tile;
    }
}
