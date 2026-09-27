using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using HanMate.App.Localization;
using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public partial class SearchPage : ContentPage
{
    private readonly LocalizationService _language;
    private readonly OfflineSearchStore _store;
    private readonly TextResourceInstaller _installer;
    private readonly ResourceManagementStore _management;
    private readonly ResourceStateStore _states;
    private readonly HanMate.Infrastructure.Catalog.BundledResourceCatalog _catalog;
    private CancellationTokenSource? _queryOperation;
    private int _generation;
    private long _epoch = -1;
    private bool _updatingSources, _opening, _subscribed, _busy;
    private IReadOnlyList<SearchSource> _sources = [];
    private WordSearchPage? _page;
    private readonly ObservableCollection<ResultRow> _rows = [];
    private readonly HashSet<Guid> _loadedIds = [];
    private bool _visible, _queryFailed;
    private string? _renderedCulture;
    private string? _sourceId;
    private readonly HandwritingSearchView _handwriting;
    private bool _handwritingMode;

    public SearchPage(LocalizationService language, OfflineSearchStore store, TextResourceInstaller installer,
        HanMate.Infrastructure.Catalog.BundledResourceCatalog catalog, ResourceManagementStore management, ResourceStateStore states)
    {
        InitializeComponent(); BindingContext = language;
        _language = language; _store = store; _installer = installer; _catalog = catalog;
        if (DeviceInfo.Idiom == DeviceIdiom.Phone) PlaceModeSwitcherInMobileTitle();
        _management = management; _states = states;
        _handwriting = new(language, store); HandwritingHost.Content = _handwriting;
        Results.ItemTemplate = new DataTemplate(() =>
        {
            var headword = new Label { FontSize = 23 }; headword.SetBinding(Label.TextProperty, nameof(ResultRow.Headword));
            var pinyin = new Label { FontSize = 15 }; pinyin.SetBinding(Label.TextProperty, nameof(ResultRow.Pinyin));
            pinyin.SetBinding(IsVisibleProperty, nameof(ResultRow.HasPinyin));
            var definition = new Label { FontSize = 16, MaxLines = 3, LineBreakMode = LineBreakMode.TailTruncation }; definition.SetBinding(Label.TextProperty, nameof(ResultRow.Definition));
            definition.SetBinding(IsVisibleProperty, nameof(ResultRow.HasDefinition));
            var translation = new Label { FontSize = 15 }; translation.SetBinding(Label.TextProperty, nameof(ResultRow.Translation));
            translation.SetBinding(IsVisibleProperty, nameof(ResultRow.HasTranslation));
            var source = new Label { FontSize = 12 }; source.SetBinding(Label.TextProperty, nameof(ResultRow.Source));
            return new VerticalStackLayout { Padding = new Thickness(8, 12), Spacing = 4,
                Children = { headword, pinyin, definition, translation, source } };
        });
        Results.ItemsSource = _rows;
        RefreshCopy();
    }

    private void PlaceModeSwitcherInMobileTitle()
    {
        SearchLayout.Children.Remove(ModeSwitcher);
        SearchLayout.RowDefinitions[0].Height = new GridLength(0);
        SearchLayout.RowSpacing = 0;
        SearchLayout.Padding = new Thickness(12, 4);

        var title = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center };
        title.SetBinding(Label.TextProperty, nameof(LocalizationService.NavSearch));
        var titleBar = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star) },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            BindingContext = _language
        };
        ModeSwitcher.HorizontalOptions = LayoutOptions.Center;
        titleBar.Add(title);
        titleBar.Add(ModeSwitcher, 1);
        Shell.SetTitleView(this, titleBar);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null && !_subscribed) { _language.PropertyChanged += OnLanguageChanged; _subscribed = true; }
        else if (Handler is null && _subscribed) { _language.PropertyChanged -= OnLanguageChanged; _subscribed = false; CancelPending(); }
    }
    protected override async void OnAppearing()
    { base.OnAppearing(); _visible = true; _ = DictionaryEntryPage.PrepareAsync(_store); await RefreshIfChangedAsync(); }
    protected override void OnDisappearing() { _visible = false; base.OnDisappearing(); CancelPending(); }
    internal void CancelPending() { _generation++; _queryOperation?.Cancel(); _queryOperation = null; _busy = false; _handwriting.Suspend(); }
    internal async Task RefreshIfChangedAsync()
    {
        RefreshCopy(); RenderRows();
        _handwriting.Prepare();
        // Recognition must not wait behind the first catalog/index initialization.
        var recognition = _handwritingMode ? _handwriting.ActivateAsync() : Task.CompletedTask;
        var generation = _generation;
        try
        {
            await Task.Run(() => _catalog.EnsureInstalledAsync());
            var epoch = await Task.Run(() => _store.GetEpochAsync());
            if (generation != _generation) return;
            if (_handwritingMode) { await recognition; return; }
            if (_epoch != epoch || _page is null && !string.IsNullOrWhiteSpace(QueryInput.Text)) await RunQueryAsync();
        }
        catch (Exception) { Status.Text = T("Failed"); }
    }
    private string T(string key) => _language["Search." + key];
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Android font changes recreate the window; an old retained handler must not repaint.
        // The current page refreshes on appearing/Shell navigation.
        if (Shell.Current?.CurrentPage != this) return;
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LocalizationService.CurrentLanguage)) { RefreshCopy(); RenderRows(); }
    }
    private void RefreshCopy()
    {
        Manage.Text = _language.ManageDictionaries;
        IndexHint.Text = T("IndexHint");
        Sources.Title = T("Sources"); RenderSources(); UpdateStatus();
        TextMode.Text = T("TextMode"); HandwritingMode.Text = T("HandwritingMode");
        TextMode.LineBreakMode = HandwritingMode.LineBreakMode = Manage.LineBreakMode = LineBreakMode.WordWrap;
        SetModeAppearance(TextMode, !_handwritingMode);
        SetModeAppearance(HandwritingMode, _handwritingMode);
        _handwriting.RefreshCopy();
    }
    private static void SetModeAppearance(Button button, bool selected)
    {
        button.SetAppThemeColor(Button.BackgroundColorProperty,
            selected ? Color.FromArgb("#512BD4") : Colors.Transparent,
            selected ? Color.FromArgb("#6B4AE8") : Colors.Transparent);
        button.SetAppThemeColor(Button.TextColorProperty,
            selected ? Colors.White : Color.FromArgb("#49358B"),
            selected ? Colors.White : Color.FromArgb("#E9DFFF"));
    }
    private async void OnTextMode(object? sender, EventArgs e) => await SetModeAsync(false);
    private async void OnHandwritingMode(object? sender, EventArgs e) => await SetModeAsync(true);
    private async Task SetModeAsync(bool handwriting)
    {
        if (_handwritingMode == handwriting) return;
        CancelPending(); _handwritingMode = handwriting; QueryInput.Unfocus();
        TextSearch.IsVisible = !handwriting; HandwritingHost.IsVisible = handwriting; RefreshCopy();
        await RefreshIfChangedAsync();
    }
    private void RenderSources()
    {
        _updatingSources = true;
        Sources.ItemsSource = new[] { T("AllSources") }.Concat(_sources.Select(s => s.Id == "personal" ? T("Personal") : s.Name)).ToArray();
        Sources.SelectedIndex = _sourceId is null ? 0 : _sources.ToList().FindIndex(s => s.Id == _sourceId) + 1;
        _updatingSources = false;
    }
    private async void OnQueryChanged(object? sender, TextChangedEventArgs e) => await RunQueryAsync(debounce: true);
    private async void OnSubmit(object? sender, EventArgs e) => await RunQueryAsync();
    private async void OnSourceChanged(object? sender, EventArgs e)
    {
        if (_updatingSources) return;
        _sourceId = Sources.SelectedIndex <= 0 ? null : _sources[Sources.SelectedIndex - 1].Id;
        await RunQueryAsync();
    }
    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_visible || _handwritingMode || _busy || _opening || _page?.Next is null) return;
        await RunQueryAsync(loadMore: true);
    }
    private async Task RunQueryAsync(bool debounce = false, bool loadMore = false)
    {
        if (_handwritingMode) return;
        _queryOperation?.Cancel();
        var generation = ++_generation;
        using var operation = new CancellationTokenSource(); _queryOperation = operation;
        var token = operation.Token;
        var input = QueryInput.Text ?? "";
        var cursor = loadMore ? _page?.Next : null;
        _busy = true; _queryFailed = false;
        if (!loadMore) { _page = null; _rows.Clear(); _loadedIds.Clear(); }
        UpdateStatus();
        try
        {
            if (debounce && !string.IsNullOrWhiteSpace(input)) await Task.Delay(250, token);
            var sources = await Task.Run(() => _store.GetSourcesAsync(token), token);
            if (generation != _generation) return;
            _sources = sources;
            if (_sourceId is not null && !sources.Any(s => s.Id == _sourceId))
            { _sourceId = null; cursor = null; _rows.Clear(); _loadedIds.Clear(); }
            RenderSources();
            var result = await Task.Run(() => _store.SearchAsync(input, _sourceId, cursor, token), token);
            var currentEpoch = await Task.Run(() => _store.GetEpochAsync(token), token);
            token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            if (result.Epoch != currentEpoch) { await RunQueryAsync(); return; }
            _page = result; _epoch = result.Epoch;
            foreach (var item in result.Items)
            {
                if (_loadedIds.Contains(item.ContentId)) continue;
                if (ToRow(item) is not { } row) continue;
                _loadedIds.Add(item.ContentId);
                _rows.Add(row);
            }
            _renderedCulture = _language.CurrentCultureName;
        }
        catch (OperationCanceledException) { }
        catch (SearchCursorStaleException) { if (generation == _generation) await RunQueryAsync(); }
        catch (Exception) { if (generation == _generation) _queryFailed = true; }
        finally
        {
            if (generation == _generation) { _busy = false; _queryOperation = null; UpdateStatus(); }
        }
    }
    private void RenderRows()
    {
        if (_renderedCulture == _language.CurrentCultureName) return;
        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (ToRow(_rows[i].Item) is { } row) _rows[i] = row;
            else _rows.RemoveAt(i);
        }
        _renderedCulture = _language.CurrentCultureName;
    }
    private ResultRow? ToRow(WordSearchResult item)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ContentDocument>(item.BodyJson, ContentJson.Options);
            if (document is null) return null;
            var headword = document.TextUnits.FirstOrDefault(u => u.Role == TextUnitRole.Headword);
            if (headword is null) return null;
            var definition = document.TextUnits.FirstOrDefault(u => u.Role == TextUnitRole.Definition);
            var lang = _language.CurrentCultureName;
            var translation = _language.ShowAuxiliaryTranslations
                ? definition?.Translations.GetValueOrDefault(lang) ?? headword.Translations.GetValueOrDefault(lang) ?? "" : "";
            return new ResultRow(item, headword.Text, string.Join(' ', headword.Tokens.Select(t => t.Pinyin?.Display).OfType<string>()),
                definition?.Text ?? "", translation, item.SourceId == "personal" ? T("Personal") : item.SourceName);
        }
        catch (JsonException) { return null; }
    }
    private void UpdateStatus()
    {
        Status.Text = _busy ? T("Searching") : string.IsNullOrWhiteSpace(QueryInput.Text) ? T("Hint")
            : _queryFailed || _page is null ? T("Failed") : _page.Total == 0 ? _language.StateNoResults
            : string.Format(T("Count"), 1, _rows.Count, _page.Total);
    }
    private async void OnManage(object? sender, EventArgs e)
    { CancelPending(); await Navigation.PushAsync(new ResourceManagementPage(_management, _states, _catalog, _installer, _language, HanMate.Core.Resources.ResourceKind.Dictionary)); }
    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_busy || _opening || e.CurrentSelection.FirstOrDefault() is not ResultRow row) return;
        Results.SelectedItem = null; _opening = true;
        var generation = _generation;
        try
        {
            var document = await Task.Run(() => JsonSerializer.Deserialize<ContentDocument>(row.Item.BodyJson, ContentJson.Options));
            if (document is null) return;
            if (generation != _generation) return;
            if (await Task.Run(() => _store.GetEpochAsync()) != _epoch) { await RunQueryAsync(); return; }
            if (generation != _generation) return;
            if (Handler?.MauiContext?.Services is not { } services) return;
            await Navigation.PushAsync(DictionaryEntryPage.Create(document, _language, !row.Item.IsReadOnly, services));
        }
        catch (Exception) { Status.Text = T("Failed"); }
        finally { _opening = false; }
    }
    private sealed record ResultRow(WordSearchResult Item, string Headword, string Pinyin, string Definition, string Translation, string Source)
    {
        public bool HasPinyin => Pinyin.Length > 0;
        public bool HasDefinition => Definition.Length > 0;
        public bool HasTranslation => Translation.Length > 0;
    }
}
