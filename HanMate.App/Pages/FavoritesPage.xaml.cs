using System.Text.Json;
using HanMate.App.Localization;
using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class FavoritesPage(FavoriteStore store, LocalizationService language, IServiceProvider services) : DataPage(language)
{
    private Guid? _folderId;
    private int _offset;
    private IReadOnlyList<FavoriteFolder>? _renderedFolders;
    private IReadOnlyList<FavoriteEntry>? _renderedEntries;
    private int _renderedBookmarkCount;
    private string? _renderedCulture;

    protected override async Task RefreshAsync()
    {
        if (_folderId is { } folder && _renderedFolders is not null && _renderedEntries is not null &&
            _renderedCulture == Language.CurrentCultureName)
        {
            var folders = await Task.Run(() => store.GetFoldersAsync());
            var entries = await Task.Run(() => store.GetEntriesAsync(folder, _offset));
            var bookmarkCount = (await services.GetRequiredService<DictionaryBookmarkStore>().GetAsync()).Count;
            if (folders.SequenceEqual(_renderedFolders) && entries.SequenceEqual(_renderedEntries) &&
                bookmarkCount == _renderedBookmarkCount) return;
        }
        await ReloadAsync();
    }
    protected override async Task ReloadAsync()
    {
        Status = LibraryLayout.Status();
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        Title = Language.NavFavorites;
        var folders = await Task.Run(() => store.GetFoldersAsync());
        var selected = folders.FirstOrDefault(f => f.Id == _folderId) ?? folders.First(f => f.IsDefault); _folderId = selected.Id;
        var entries = await Task.Run(() => store.GetEntriesAsync(selected.Id, _offset));
        if (entries.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        var picker = new Picker { Title = Language.NavFavorites, AutomationId = "Favorites.Folders",
            ItemsSource = folders.Select(f => (f.IsDefault ? Language.DefaultFolderName : f.Name) + $" ({f.Count})").ToArray(),
            SelectedIndex = folders.ToList().IndexOf(selected), MinimumHeightRequest = 48 };
        SemanticProperties.SetDescription(picker, Language.NavFavorites);
        picker.SelectedIndexChanged += async (_, _) =>
        { if (picker.SelectedIndex >= 0) await RunAsync(async () => { _folderId = folders[picker.SelectedIndex].Id; _offset = 0; await ReloadAsync(); }); };
        var more = LibraryLayout.Quiet(Button("More", () => ManageFolderAsync(selected, folders.ToList().IndexOf(selected), folders.Count)));
        more.Text = "⋯"; more.FontSize = 24; more.WidthRequest = 48; more.AutomationId = "Favorites.FolderActions";
        SemanticProperties.SetDescription(more, T("FolderActions")); ToolTipProperties.SetText(more, T("FolderActions"));
        var chooser = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        chooser.Add(picker); chooser.Add(more, 1);
        var header = new VerticalStackLayout { Spacing = 8 };
        var bookmarks = services.GetRequiredService<DictionaryBookmarkStore>();
        var bookmarkCount = (await bookmarks.GetAsync()).Count;
        if (bookmarkCount > 0)
        {
            var dictionary = new NavigationTile { Title = T("DictionaryFavorites") + $" ({bookmarkCount})", Compact = true, ActionId = "Favorites.Dictionary" };
            dictionary.Clicked += async (_, _) => await RunAsync(async () => await Navigation.PushAsync(new DictionaryBookmarksPage(bookmarks, Language)));
            header.Add(dictionary);
        }
        header.Add(LibraryLayout.Surface(chooser, new Thickness(12, 4)));
        if (!string.IsNullOrWhiteSpace(selected.Description)) header.Add(LibraryLayout.Muted(selected.Description));
        if (entries.Count > 0) header.Add(LibraryLayout.Muted(string.Format(T("Count"), _offset + 1, _offset + entries.Count, selected.Count)));
        var rows = new CollectionView { AutomationId = "Favorites.Items", SelectionMode = SelectionMode.None, ItemsSource = entries,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            EmptyView = new Label { Text = T("EmptyFavorites"), Margin = new Thickness(24, 40), HorizontalTextAlignment = TextAlignment.Center },
            ItemTemplate = new DataTemplate(() =>
            {
                var state = LibraryLayout.Muted(""); state.IsVisible = false;
                state.BindingContextChanged += (_, _) =>
                { if (state.BindingContext is FavoriteEntry entry) { state.Text = entry.SourceState == "Ready" ? "" : T(entry.SourceState); state.IsVisible = entry.SourceState != "Ready"; } };
                var actions = LibraryLayout.Quiet(new Button { Text = "⋯", FontSize = 24, WidthRequest = 48, Margin = new Thickness(0, 0, 4, 0) });
                SemanticProperties.SetDescription(actions, T("More"));
                actions.BindingContextChanged += (_, _) =>
                { if (actions.BindingContext is FavoriteEntry entry) SemanticProperties.SetDescription(actions, T("More") + " · " + entry.Title); };
                actions.Clicked += async (_, _) =>
                {
                    if (actions.BindingContext is not FavoriteEntry entry) return;
                    await RunAsync(async () =>
                    {
                        var choice = await DisplayActionSheetAsync(entry.Title, T("Cancel"), null, T("Read"), T("ChooseFolders"));
                        if (choice == T("Read")) await ReadAsync(entry);
                        else if (choice == T("ChooseFolders")) await Navigation.PushAsync(new FavoritePickerPage(store, Language, entry.Id));
                    });
                };
                return LibraryLayout.Surface(LibraryLayout.ContentRow(nameof(FavoriteEntry.Title), open =>
                {
                    open.AutomationId = "Favorites.Open";
                    SemanticProperties.SetHint(open, T("Read"));
                    open.Clicked += async (_, _) => { if (open.BindingContext is FavoriteEntry entry) await RunAsync(() => ReadAsync(entry)); };
                }, actions, state));
            }) };
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = _offset + 50 < selected.Count;
        previous.AutomationId = "Favorites.Previous"; next.AutomationId = "Favorites.Next";
        var grid = new Grid { Padding = 16, RowSpacing = 12, MaximumWidthRequest = 920,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        var headerScroll = new ScrollView { Content = header, MaximumHeightRequest = 220 };
        grid.SizeChanged += (_, _) => { if (grid.Height > 0) headerScroll.MaximumHeightRequest = Math.Min(220, grid.Height * .35); };
        grid.Add(headerScroll); grid.Add(Status, 0, 1); grid.Add(rows, 0, 2);
        grid.Add(LibraryLayout.Paging(previous, next, selected.Count > 50), 0, 3); Content = grid;
        _renderedFolders = folders; _renderedEntries = entries;
        _renderedBookmarkCount = bookmarkCount; _renderedCulture = Language.CurrentCultureName;
    }
    private async Task ReadAsync(FavoriteEntry entry)
    {
        var document = await Task.Run(() => JsonSerializer.Deserialize<ContentDocument>(entry.BodyJson, ContentJson.Options)
            ?? throw new InvalidDataException("Favorite content is unavailable."));
        await Navigation.PushAsync(await LearningDetailPageFactory.CreateAsync(document, Language, services));
    }

    private async Task ManageFolderAsync(FavoriteFolder selected, int index, int count)
    {
        var options = new List<string> { T("NewFolder") };
        if (!selected.IsDefault) options.Add(T("EditFolder"));
        if (index > 0) options.Add(T("Up"));
        if (index + 1 < count) options.Add(T("Down"));
        options.Add(T("Refresh"));
        var choice = await DisplayActionSheetAsync(T("FolderActions"), T("Cancel"), selected.IsDefault ? null : T("DeleteFolder"), options.ToArray());
        if (choice == T("NewFolder")) await EditAsync(null);
        else if (choice == T("EditFolder")) await EditAsync(selected);
        else if (choice == T("DeleteFolder") && !selected.IsDefault)
        {
            if (!await DisplayAlertAsync(T("DeleteFolder"), T("DeleteFolderHint"), T("Delete"), T("Cancel"))) return;
            await Task.Run(() => store.DeleteFolderAsync(selected)); _folderId = null; _offset = 0; await ReloadAsync();
        }
        else if (choice == T("Up") || choice == T("Down"))
        { await Task.Run(() => store.MoveAsync(selected.Id, choice == T("Up") ? -1 : 1)); await ReloadAsync(); }
        else if (choice == T("Refresh")) await ReloadAsync();
    }
    private async Task EditAsync(FavoriteFolder? folder)
    {
        var name = await DisplayPromptAsync(T("FolderName"), T("FolderNameHint"), T("Save"), T("Cancel"), initialValue: folder?.Name ?? "");
        if (name is null) return;
        var description = await DisplayPromptAsync(T("Description"), T("DescriptionHint"), T("Save"), T("Cancel"), initialValue: folder?.Description ?? "");
        if (description is null) return;
        _folderId = await Task.Run(() => store.SaveFolderAsync(name, description, folder)); _offset = 0; await ReloadAsync();
    }
}

public sealed class FavoritePickerPage(FavoriteStore store, LocalizationService language, Guid contentId) : DataPage(language)
{
    protected override async Task ReloadAsync()
    {
        Status = LibraryLayout.Status();
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        Title = T("ChooseFolders");
        var folders = await Task.Run(() => store.GetFoldersAsync()); var selection = await Task.Run(() => store.GetSelectionAsync(contentId));
        var chosen = selection.FolderIds.ToHashSet();
        var body = new VerticalStackLayout { Spacing = 8 }; body.Add(LibraryLayout.Muted(T("ChooseHint")));
        foreach (var folder in folders)
        {
            var box = new CheckBox { IsChecked = chosen.Contains(folder.Id) };
            box.CheckedChanged += (_, e) => { if (e.Value) chosen.Add(folder.Id); else chosen.Remove(folder.Id); };
            var name = folder.IsDefault ? Language.DefaultFolderName : folder.Name;
            SemanticProperties.SetDescription(box, name);
            var row = new Grid { ColumnSpacing = 8, Padding = 8, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            row.Add(box); row.Add(new Label { Text = name, FontSize = 16, LineBreakMode = LineBreakMode.WordWrap, VerticalTextAlignment = TextAlignment.Center }, 1);
            body.Add(LibraryLayout.Surface(row));
        }
        var save = Button("Save", async () => { await Task.Run(() => store.SetSelectionAsync(contentId, chosen.ToArray(), selection.Revision)); await Navigation.PopAsync(); });
        save.MinimumHeightRequest = 48; save.LineBreakMode = LineBreakMode.WordWrap;
        var refresh = LibraryLayout.Quiet(Button("Refresh", ReloadAsync));
        var footer = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        footer.Add(refresh); footer.Add(save, 1);
        var grid = new Grid { Padding = 20, RowSpacing = 12, MaximumWidthRequest = 760,
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(new ScrollView { Content = body }); grid.Add(Status, 0, 1); grid.Add(footer, 0, 2); Content = grid;
    }
}
