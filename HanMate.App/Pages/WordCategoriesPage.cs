using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class WordCategoriesPage(LearningCatalogStore store, CustomWordCategoryStore custom, LocalizationService language) : DataPage(language)
{
    private WordCategoryCounts? _renderedCounts;
    private string? _renderedCulture;
    private IReadOnlyList<CustomWordCategory> _renderedCustom = [];
    private IReadOnlyList<BuiltInWordCategoryPreference> _renderedBuiltIn = [];
    private readonly Dictionary<string, LearningBrowserPage> _browsers = [];
    protected override async Task RefreshAsync()
    {
        if (_renderedCounts is not null && _renderedCulture == Language.CurrentCultureName)
        {
            var counts = await Task.Run(() => store.GetWordCategoryCountsAsync());
            var categories = await Task.Run(() => custom.ListAsync());
            var builtIn = await Task.Run(() => custom.ListBuiltInAsync());
            if (counts.Total == _renderedCounts.Total && counts.Counts.Count == _renderedCounts.Counts.Count &&
                counts.Counts.All(p => _renderedCounts.Counts.TryGetValue(p.Key, out var value) && value == p.Value) &&
                categories.SequenceEqual(_renderedCustom) && builtIn.SequenceEqual(_renderedBuiltIn)) return;
        }
        await ReloadAsync();
    }
    protected override async Task ReloadAsync()
    {
        Title = Language.KindWord;
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        Status = LibraryLayout.Status();
        var counts = await Task.Run(() => store.GetWordCategoryCountsAsync());
        var categories = await Task.Run(() => custom.ListAsync());
        var builtIn = await Task.Run(() => custom.ListBuiltInAsync());
        var overrides = builtIn.ToDictionary(x => x.Id);
        var body = new VerticalStackLayout { Padding = 20, Spacing = 16, MaximumWidthRequest = 920 };
        body.Add(new Label { Text = Language["WordCategories.Title"], FontSize = 24, FontAttributes = FontAttributes.Bold });
        body.Add(LibraryLayout.Muted(Language["WordCategories.Hint"]));
        body.Add(Tile("all", counts.Total, compact: true));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var category in WordCategories.All)
            if (overrides.GetValueOrDefault(category)?.Hidden != true)
                grid.Children.Add(BuiltInTile(category, counts.Counts.GetValueOrDefault(category), overrides.GetValueOrDefault(category)?.Name));
        if (counts.Counts.GetValueOrDefault(WordCategories.Other) > 0 &&
            overrides.GetValueOrDefault(WordCategories.Other)?.Hidden != true)
            grid.Children.Add(BuiltInTile(WordCategories.Other, counts.Counts[WordCategories.Other], overrides.GetValueOrDefault(WordCategories.Other)?.Name));
        foreach (var category in categories)
        {
            var tile = Tile(category.Id, counts.Counts.GetValueOrDefault(category.Id), title: category.Name);
            tile.LongPressed += async (_, _) => await RunAsync(async () =>
            {
                var action = await DisplayActionSheetAsync(category.Name, T("Cancel"), null,
                    Language["WordCategories.Edit"], T("Delete"));
                if (action == Language["WordCategories.Edit"])
                {
                    var name = await DisplayPromptAsync(Language["WordCategories.Edit"], Language["WordCategories.Name"], T("Save"), T("Cancel"), initialValue: category.Name, maxLength: 40);
                    if (name is null) return;
                    await custom.RenameAsync(category.Id, name); _browsers.Remove(category.Id); await ReloadAsync();
                }
                else if (action == T("Delete"))
                {
                    if (!await DisplayAlertAsync(T("Delete"), Language["WordCategories.DeleteHint"], T("Delete"), T("Cancel"))) return;
                    await custom.DeleteAsync(category.Id); _browsers.Remove(category.Id); await ReloadAsync();
                }
            });
            grid.Children.Add(tile);
        }
        var create = new NavigationTile { Title = Language["WordCategories.New"], Detail = Language["WordCategories.NewHint"], ActionId = "WordCategories.New" };
        create.Clicked += async (_, _) => await RunAsync(async () =>
        {
            var name = await DisplayPromptAsync(Language["WordCategories.New"], Language["WordCategories.Name"], T("Save"), T("Cancel"), maxLength: 40);
            if (name is null) return;
            var category = await custom.CreateAsync(name);
            await ReloadAsync();
            _browsers[category.Id] = new LearningBrowserPage(store, Language, ContentKind.Word, category.Id, category.Name);
            await Navigation.PushAsync(_browsers[category.Id]);
        });
        grid.Children.Add(create);
        var restores = new VerticalStackLayout { Spacing = 8 };
        foreach (var preference in builtIn.Where(x => x.Hidden))
        {
            var label = preference.Name ?? Language["WordCategories." + preference.Id];
            var restore = new Button { Text = Language["WordCategories.Restore"] + " · " + label,
                AutomationId = "WordCategories.Restore." + preference.Id };
            restore.Clicked += async (_, _) => await RunAsync(async () =>
            {
                await custom.SetBuiltInHiddenAsync(preference.Id, false); await ReloadAsync();
            });
            restores.Add(restore);
        }
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
        body.Add(grid); if (restores.Children.Count > 0) body.Add(restores); body.Add(Status);
        Content = new ScrollView { AutomationId = "WordCategories.Scroll", Content = body };
        _renderedCounts = counts; _renderedCulture = Language.CurrentCultureName;
        _renderedCustom = categories; _renderedBuiltIn = builtIn;
    }

    private NavigationTile BuiltInTile(string category, int count, string? name)
    {
        var tile = Tile(category, count, title: name);
        tile.LongPressed += async (_, _) => await RunAsync(async () =>
        {
            var label = name ?? Language["WordCategories." + category];
            var action = await DisplayActionSheetAsync(label, T("Cancel"), null,
                Language["WordCategories.Edit"], T("Delete"));
            if (action == Language["WordCategories.Edit"])
            {
                var changed = await DisplayPromptAsync(Language["WordCategories.Edit"], Language["WordCategories.Name"],
                    T("Save"), T("Cancel"), initialValue: label, maxLength: 40);
                if (changed is null) return;
                await custom.RenameBuiltInAsync(category, changed); _browsers.Remove(category); await ReloadAsync();
            }
            else if (action == T("Delete"))
            {
                if (!await DisplayAlertAsync(T("Delete"), Language["WordCategories.HideBuiltInHint"],
                    T("Delete"), T("Cancel"))) return;
                await custom.SetBuiltInHiddenAsync(category, true); _browsers.Remove(category); await ReloadAsync();
            }
        });
        return tile;
    }

    private NavigationTile Tile(string category, int count, bool compact = false, string? title = null)
    {
        var tile = new NavigationTile
        {
            Title = title ?? Language["WordCategories." + category], Compact = compact,
            Detail = string.Format(Language["WordCategories.Count"], count),
            ActionId = "WordCategories." + category
        };
        tile.Clicked += async (_, _) => await RunAsync(() =>
        {
            if (!_browsers.TryGetValue(category, out var page))
                _browsers[category] = page = new LearningBrowserPage(store, Language, ContentKind.Word, category == "all" ? null : category, title);
            return Navigation.PushAsync(page);
        });
        return tile;
    }
}
