using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Dictionary;

namespace HanMate.App.Pages;

public sealed class DictionaryBookmarksPage(DictionaryBookmarkStore store, LocalizationService language) : DataPage(language)
{
    private int _offset;
    protected override async Task ReloadAsync()
    {
        Title = T("DictionaryFavorites"); Status = LibraryLayout.Status(); this.SetDynamicResource(StyleProperty, "LibraryPage");
        var all = await store.GetAsync();
        if (_offset >= all.Count) _offset = Math.Max(0, (all.Count - 1) / 50 * 50);
        var entries = all.Reverse().Skip(_offset).Take(50).ToArray();
        var list = new CollectionView { SelectionMode = SelectionMode.None, AutomationId = "DictionaryFavorites.Items", ItemsSource = entries,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            EmptyView = new Label { Text = T("EmptyFavorites"), Margin = 24 },
            ItemTemplate = new DataTemplate(() =>
            {
                var more = LibraryLayout.Quiet(new Button { Text = "⋯", FontSize = 24, WidthRequest = 48 });
                SemanticProperties.SetDescription(more, T("More"));
                more.Clicked += async (_, _) =>
                {
                    if (more.BindingContext is not DictionaryBookmark entry) return;
                    await RunAsync(async () =>
                    {
                        var remove = Language["DictionaryDetail.RemoveFavorite"];
                        if (await DisplayActionSheetAsync(entry.Title, T("Cancel"), null, remove) == remove)
                        { await store.SetAsync(entry, false); await ReloadAsync(); }
                    });
                };
                return LibraryLayout.Surface(LibraryLayout.ContentRow(nameof(DictionaryBookmark.Title), open =>
                {
                    SemanticProperties.SetHint(open, T("Read"));
                    open.Clicked += async (_, _) =>
                    { if (open.BindingContext is DictionaryBookmark entry) await RunAsync(() => OpenAsync(entry)); };
                }, more));
            }) };
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = _offset + 50 < all.Count;
        var grid = new Grid { Padding = 16, RowSpacing = 12, MaximumWidthRequest = 920,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        grid.Add(Status); grid.Add(list, 0, 1); grid.Add(LibraryLayout.Paging(previous, next, all.Count > 50), 0, 2); Content = grid;
    }
    private async Task OpenAsync(DictionaryBookmark entry)
    {
        try
        {
            var services = Handler!.MauiContext!.Services;
            var document = entry.Provider == DictionaryBookmarkStore.Xinhua
                ? await services.GetRequiredService<DefaultDictionaryStore>().GetAsync(entry.EntryId)
                : (await services.GetRequiredService<BundledPronunciationService>().GetCourseAsync()).Data.Contents.FirstOrDefault(d => d.Id == entry.EntryId)
                    ?? throw new KeyNotFoundException();
            await Navigation.PushAsync(new DictionaryEntryPage(document, Language, services));
        }
        catch (KeyNotFoundException) { Status.Text = Language["DictionaryDetail.FavoriteMissing"]; }
    }
}
