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
    private readonly HashSet<string> _selectedCategories = [];
    private readonly ToolbarItem _more = new() { Text = "⋯", Order = ToolbarItemOrder.Primary,
        AutomationId = "WordCategories.More" };
    private bool _moreReady, _selecting;
    private ImageButton? _shareAction;
    private ActivityIndicator? _preparing;
    private readonly Dictionary<string, (NavigationTile Tile, string Label)> _categoryTiles = [];
    private NavigationTile? _createTile;
    private VerticalStackLayout? _restoreActions;
    private VerticalStackLayout? _body;
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
        if (!_moreReady)
        {
            _moreReady = true;
            _more.Clicked += async (_, _) => await MoreAsync();
            ToolbarItems.Add(_more);
        }
        SemanticProperties.SetDescription(_more, Language["DictionaryDetail.More"]);
        Status = LibraryLayout.Status();
        var counts = await Task.Run(() => store.GetWordCategoryCountsAsync());
        var categories = await Task.Run(() => custom.ListAsync());
        var builtIn = await Task.Run(() => custom.ListBuiltInAsync());
        var overrides = builtIn.ToDictionary(x => x.Id);
        _categoryTiles.Clear();
        var body = new VerticalStackLayout { Padding = new Thickness(20, 20, 20, _selecting ? 88 : 20),
            Spacing = 16, MaximumWidthRequest = 920 };
        _body = body;
        _preparing = new ActivityIndicator { IsVisible = false, HorizontalOptions = LayoutOptions.Start };
        body.Add(_preparing);
        body.Add(Status);
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
                if (_selecting) return;
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
        _createTile = create;
        create.IsVisible = !_selecting;
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
        _restoreActions = restores;
        restores.IsVisible = !_selecting;
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
        body.Add(grid); if (restores.Children.Count > 0) body.Add(restores);
        var surface = new Grid();
        surface.Add(new ScrollView { AutomationId = "WordCategories.Scroll", Content = body });
        var icon = new ImageButton { Source = "share.png", BackgroundColor = Color.FromArgb("#4056A1"),
            CornerRadius = 28, WidthRequest = 56, HeightRequest = 56, Padding = 14,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 22, 22), AutomationId = "WordCategories.ShareSelected",
            IsVisible = _selecting };
        icon.Clicked += async (_, _) => await ShareSelectedAsync();
        SemanticProperties.SetDescription(icon, Language["LanShare.Share"]);
        _shareAction = icon; surface.Add(icon); UpdateShareAction();
        Content = surface;
        _renderedCounts = counts; _renderedCulture = Language.CurrentCultureName;
        _renderedCustom = categories; _renderedBuiltIn = builtIn;
    }

    private NavigationTile BuiltInTile(string category, int count, string? name)
    {
        var tile = Tile(category, count, title: name);
        tile.LongPressed += async (_, _) => await RunAsync(async () =>
        {
            if (_selecting) return;
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
        var label = title ?? Language["WordCategories." + category];
        var tile = new NavigationTile
        {
            Title = _selecting ? (_selectedCategories.Contains(category) ? "☑ " : "□ ") + label : label,
            Compact = compact,
            Detail = string.Format(Language["WordCategories.Count"], count),
            ActionId = "WordCategories." + category
        };
        _categoryTiles[category] = (tile, label);
        tile.Clicked += async (_, _) =>
        {
            if (_selecting)
            {
                if (Busy) return;
                if (!_selectedCategories.Add(category)) _selectedCategories.Remove(category);
                tile.Title = (_selectedCategories.Contains(category) ? "☑ " : "□ ") + label;
                UpdateShareAction();
                return;
            }
            await RunAsync(() =>
            {
                if (!_browsers.TryGetValue(category, out var page))
                    _browsers[category] = page = new LearningBrowserPage(store, Language, ContentKind.Word, category == "all" ? null : category, title);
                return Navigation.PushAsync(page);
            });
        };
        return tile;
    }

    private async Task MoreAsync()
    {
        if (Busy) return;
        var select = Language["LanShare.SelectCategories"];
        var exit = Language["LanShare.CancelSelection"];
        var choice = await DisplayActionSheetAsync(Language["DictionaryDetail.More"], T("Cancel"), null,
            _selecting ? exit : select);
        if (choice != select && choice != exit) return;
        SetSelectionMode(choice == select);
    }

    private void SetSelectionMode(bool selecting)
    {
        _selecting = selecting;
        _selectedCategories.Clear();
        foreach (var entry in _categoryTiles.Values)
            entry.Tile.Title = selecting ? "□ " + entry.Label : entry.Label;
        if (_createTile is not null) _createTile.IsVisible = !selecting;
        if (_restoreActions is not null) _restoreActions.IsVisible = !selecting;
        if (_body is not null) _body.Padding = new Thickness(20, 20, 20, selecting ? 88 : 20);
        if (_shareAction is not null) _shareAction.IsVisible = selecting;
        UpdateShareAction();
    }

    private void UpdateShareAction()
    {
        if (_shareAction is null) return;
        _shareAction.IsEnabled = _selectedCategories.Count > 0 && !Busy;
        SemanticProperties.SetDescription(_shareAction, Language["LanShare.Share"] + " · " +
            string.Format(Language["LanShare.Selected"], _selectedCategories.Count));
    }

    private async Task ShareSelectedAsync()
    {
        await RunAsync(async () =>
        {
            if (Handler?.MauiContext?.Services is not { } services) return;
            if (_preparing is not null) { _preparing.IsVisible = true; _preparing.IsRunning = true; }
            Status.Text = Language["LanShare.Preparing"];
            UpdateShareAction();
            var categories = _selectedCategories.ToArray();
            var ids = await Task.Run(() => store.GetWordIdsForCategoriesAsync(categories));
            if (ids.Count > 100) { Status.Text = Language["LanShare.Limit"]; return; }
            if (ids.Count == 0) { Status.Text = Language["LanShare.EmptyCategories"]; return; }
            if (await LearningShareRoomLauncher.OpenAsync(this, services, Language, ids))
            {
                Status.Text = "";
                SetSelectionMode(false);
            }
            else Status.Text = "";
        });
        if (_preparing is not null) { _preparing.IsRunning = false; _preparing.IsVisible = false; }
        UpdateShareAction();
    }
}
