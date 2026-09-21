using System.ComponentModel;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;

namespace HanMate.App.Pages;

public sealed partial class ReadingPage : ContentPage
{
    private readonly ReadingDocument _document;
    private readonly LocalizationService _language;
    private readonly VerticalStackLayout _body = new() { Padding = 18, Spacing = 14 };
    private readonly ScrollView _scroll;
    private readonly Button _pinyin = new() { AutomationId = "Reader.Pinyin", LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _smaller = new() { Text = "A−", AutomationId = "Reader.Smaller" };
    private readonly Button _larger = new() { Text = "A+", AutomationId = "Reader.Larger" };
    private readonly Label _scaleLabel = new() { VerticalTextAlignment = TextAlignment.Center };
    private readonly Button _previous = new() { AutomationId = "Reader.Previous", LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _next = new() { AutomationId = "Reader.Next", LineBreakMode = LineBreakMode.WordWrap };
    private readonly Picker _page = new() { AutomationId = "Reader.Page" };
    private IReadOnlyList<HanMate.Core.Reading.ReadingPage> _pages;
    private int? _targetIndex;
    private int _pageIndex;
    private double _scale;
    private bool _showPinyin = true, _refreshing, _opening;
    private bool _preferencesLoaded, _loadingPreferences, _savingPreference;
    private readonly bool _allowEditing;
    private Shell? _readerShell;
    private UiLanguage? _renderedLanguage;

    public ReadingPage(ReadingDocument document, LocalizationService language, int? targetIndex = null, double scale = 1, bool allowEditing = true, bool autoPlay = false)
    {
        _document = document; _language = language; _targetIndex = targetIndex; _scale = scale;
        _allowEditing = allowEditing;
        _autoPlay = autoPlay;
        _pages = targetIndex is { } index ? document.PagesFor(document.Targets[index]) : document.Pages;
        _scroll = new ScrollView { AutomationId = "Reader.Scroll" };
        var tools = new Grid { Padding = new Thickness(12, 8), ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) } };
        tools.Add(_pinyin, 0); tools.Add(_smaller, 1); tools.Add(_larger, 2); tools.Add(_scaleLabel, 3);
        var paging = new Grid { Padding = new Thickness(12, 8), ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        paging.Add(_previous, 0); paging.Add(_page, 1); paging.Add(_next, 2);
        // Large system fonts must not let fixed toolbars consume the reading viewport.
        // Keep page navigation available while all reading tools and explanations can scroll.
        _scroll.Content = new VerticalStackLayout { Children = { tools, CreatePlaybackControls(), _body } };
        var layout = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        layout.Add(_scroll, 0, 0); layout.Add(paging, 0, 1); Content = layout;
        _pinyin.Clicked += async (_, _) => await SavePreferenceAsync(!_showPinyin, _scale);
        _smaller.Clicked += async (_, _) => await SavePreferenceAsync(_showPinyin, Math.Max(1, _scale - .25));
        _larger.Clicked += async (_, _) => await SavePreferenceAsync(_showPinyin, Math.Min(2, _scale + .25));
        _previous.Clicked += (_, _) => ChangePage(_pageIndex - 1);
        _next.Clicked += (_, _) => ChangePage(_pageIndex + 1);
        _page.SelectedIndexChanged += (_, _) => { if (!_refreshing) ChangePage(_page.SelectedIndex); };
        Render();
    }

    private string T(string key) => _language["Reader." + key];
    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true;
        _language.PropertyChanged -= OnLanguageChanged;
        _language.PropertyChanged += OnLanguageChanged;
        // Hidden pages may have missed a language change while another tab was visible.
        _playbackStatus.Text = T("LocalPlayback"); Render(); await LoadPreferencesAsync();
        await TryAutoPlayAsync();
    }
    protected override async void OnDisappearing()
    { _active = false; _language.PropertyChanged -= OnLanguageChanged; base.OnDisappearing(); await StopReadingAsync(); }
    private async Task LoadPreferencesAsync()
    {
        if (_loadingPreferences || Handler?.MauiContext is null) return;
        _loadingPreferences = true;
        try
        {
            var store = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Database.ReadingPreferenceStore>();
            var preference = await Task.Run(() => store.GetAsync());
            if (!_active || Handler?.MauiContext is null) return;
            _showPinyin = preference.ShowPinyin;
            if (_targetIndex is null) _scale = preference.Scale; _preferencesLoaded = true; Render();
        }
        catch { /* Reading remains available with defaults when settings cannot be read. */ }
        finally { _loadingPreferences = false; }
    }
    private async Task SavePreferenceAsync(bool pinyin, double scale)
    {
        if (_savingPreference || !_preferencesLoaded) return; _savingPreference = true;
        try
        {
            var store = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Database.ReadingPreferenceStore>();
            await Task.Run(() => store.SaveAsync(new(pinyin, scale))); _showPinyin = pinyin; _scale = scale; Render();
        }
        catch { await DisplayAlertAsync(Title, _language["Library.Failed"], _language["Library.Cancel"]); }
        finally { _savingPreference = false; }
    }
    private void ChangePage(int index)
    {
        if (index < 0 || index >= _pages.Count || index == _pageIndex) return;
        _pageIndex = index; Render(); _ = _scroll.ScrollToAsync(0, 0, false);
    }

    private void Render()
    {
        _refreshing = true;
        try
        {
            Title = _document.Content.Title;
            if (_renderedLanguage != _language.CurrentLanguage) _playbackStatus.Text = T("LocalPlayback");
            _renderedLanguage = _language.CurrentLanguage;
            RefreshPlaybackControls(); _audioViews.Clear();
            _pinyin.Text = T(_showPinyin ? "HidePinyin" : "ShowPinyin");
            _scaleLabel.Text = $"{_scale * 100:0}%";
            _smaller.IsEnabled = _scale > 1; _larger.IsEnabled = _scale < 2;
            SemanticProperties.SetDescription(_smaller, T("Smaller")); SemanticProperties.SetDescription(_larger, T("Larger"));
            _previous.Text = T("Previous"); _next.Text = T("Next");
            _previous.IsEnabled = _pageIndex > 0; _next.IsEnabled = _pageIndex + 1 < _pages.Count;
            _page.Title = T("Page"); SemanticProperties.SetDescription(_page, T("Page"));
            if (_page.ItemsSource?.Count != _pages.Count)
                _page.ItemsSource = Enumerable.Range(1, _pages.Count).Select(i => $"{i} / {_pages.Count}").ToArray();
            _page.SelectedIndex = _pages.Count == 0 ? -1 : _pageIndex;
            _body.Clear();
            var management = new List<View>();
            _body.Add(new Label { Text = _document.Content.Title, FontSize = 27, FontAttributes = FontAttributes.Bold });
            var favorite = new Button { Text = _language["Library.ChooseFolders"], AutomationId = "Reader.Favorite" };
            favorite.Clicked += async (_, _) =>
            {
                if (_opening) return; _opening = true;
                try
                {
                    var store = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Database.FavoriteStore>();
                    await Navigation.PushAsync(new FavoritePickerPage(store, _language, _document.Content.Id));
                }
                catch { await DisplayAlertAsync(_language["Library.ChooseFolders"], _language["Library.Failed"], _language["Library.Cancel"]); }
                finally { _opening = false; }
            };
            if (_allowEditing) management.Add(favorite);
            if (_allowEditing && _targetIndex is null)
            {
                var share = new Button { Text = _language["Transfer.Title"] };
                share.Clicked += async (_, _) =>
                {
                    if (_opening) return; _opening = true;
                    try { await Navigation.PushAsync(ContentTransferPage.Create(Handler!.MauiContext!.Services, _language, _document.Content.Id)); }
                    finally { _opening = false; }
                };
                management.Add(share);
                var edit = new Button { Text = _language["Library." + (_document.Content.Origin == ContentOrigin.Personal ? "EditPinyin" : "CopyAndEdit")] };
                edit.Clicked += async (_, _) =>
                {
                    if (_opening) return; _opening = true;
                    try
                    {
                        var services = Handler!.MauiContext!.Services;
                        var snapshot = await services.GetRequiredService<HanMate.Infrastructure.Database.SqliteContentDocumentStore>().GetAsync(_document.Content.Id);
                        if (snapshot is null) return;
                        var draft = await services.GetRequiredService<HanMate.Infrastructure.Database.EditorCommitStore>().StartAsync(snapshot);
                        var drafts = services.GetRequiredService<HanMate.Infrastructure.Database.TextDraftStore>();
                        await Navigation.PushAsync(draft.Body.Kind == ContentKind.Grammar ? new GrammarEditorPage(drafts, _language, draft) : draft.Body.StructuredOnly ? new AnnotationPage(drafts, _language, draft) : new TextDraftPage(drafts, _language, draft));
                    }
                    catch { await DisplayAlertAsync(Title, _language["Library.Failed"], _language["Library.Cancel"]); }
                    finally { _opening = false; }
                };
                management.Add(edit);
                if (_document.Content.Origin == ContentOrigin.Personal)
                {
                    var trash = new Button { Text = _language["Library.MoveToTrash"], AutomationId = "Reader.Trash" };
                    trash.Clicked += async (_, _) =>
                    {
                        if (_opening) return; _opening = true;
                        try
                        {
                            var store = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Database.ContentTrashStore>();
                            var plan = await Task.Run(() => store.PreviewAsync(_document.Content.Id)); var d = plan.Dependencies;
                            if (!await DisplayAlertAsync(trash.Text, string.Format(_language["Library.TrashPreview"], d.Favorites, d.Audio, d.Drafts, d.Other), trash.Text, _language["Library.Cancel"])) return;
                            await Task.Run(() => store.MoveAsync(plan)); await Navigation.PopAsync();
                        }
                        catch { await DisplayAlertAsync(Title, _language["Library.Stale"], _language["Library.Cancel"]); }
                        finally { _opening = false; }
                    };
                    management.Add(trash);
                }
            }
            if (_allowEditing && _targetIndex is { } audioIndex)
            {
                var audioTarget = _document.Targets[audioIndex];
                AddAudioButton(audioTarget.SegmentId ?? audioTarget.UnitId);
            }
            if (_targetIndex is { } selected)
            {
                _body.Add(new Label { Text = string.Format(T("Selection"), selected + 1, _document.Targets.Count), FontSize = 14 });
                var previous = new Button { Text = T("PreviousSegment"), IsEnabled = selected > 0, AutomationId = "Reader.PreviousSegment", LineBreakMode = LineBreakMode.WordWrap };
                var next = new Button { Text = T("NextSegment"), IsEnabled = selected + 1 < _document.Targets.Count, AutomationId = "Reader.NextSegment", LineBreakMode = LineBreakMode.WordWrap };
                previous.Clicked += async (_, _) => await ChangeTargetAsync(selected - 1); next.Clicked += async (_, _) => await ChangeTargetAsync(selected + 1);
                var navigation = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
                navigation.Add(previous, 0); navigation.Add(next, 1); _body.Add(navigation);
            }
            else
            {
                _body.Add(new Label { Text = T("Hint"), FontSize = 14 });
                if (_pageIndex == 0 && _document.Content.Grammar is { } grammar)
                {
                    var pattern = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                    foreach (var part in grammar.PatternParts)
                        pattern.Add(new Border { Padding = 7, Margin = 2, Content = new Label { Text = part.Text, FontSize = 20,
                            FontAttributes = part.Kind == GrammarPatternPartKind.Slot ? FontAttributes.Italic : FontAttributes.Bold } });
                    _body.Add(pattern);
                    AddTranslation(grammar.PatternTranslations);
                }
            }
            if (_pages.Count == 0) _body.Add(new Label { Text = T("Empty") });
            else foreach (var part in _pages[_pageIndex].Parts)
            {
                _body.Add(new Label { Text = T("Role." + part.Unit.Role) + (part.StartsUnit ? "" : " · " + T("Continued")), FontAttributes = FontAttributes.Bold, FontSize = 14 });
                // Publisher dictionary definitions have no per-character annotation. Display
                // the original text without adding rows of unknown-pronunciation markers.
                var publisherDictionary = _document.Content.Source.SourceId == HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.SourceId;
                var ruby = new RubyTextView(part.Atoms, _showPinyin && (!publisherDictionary || part.Unit.Role == TextUnitRole.Headword), _scale, atom => ActivateTarget(_document.TargetFor(atom)));
                ruby.SetPlayingTarget(_playingTarget); _audioViews.Add(ruby); _body.Add(ruby);
                if (_allowEditing && _targetIndex is null && part.StartsUnit && _document.Content.Kind is ContentKind.Word or ContentKind.Grammar)
                {
                    var playUnit = new Button { Text = T("PlayUnit"), AutomationId = "Reader.PlayUnit." + part.Unit.Id };
                    playUnit.Clicked += async (_, _) => await PlayReadingAsync(new(part.Unit.Id, null)); _body.Add(playUnit);
                }
                if (_allowEditing && _targetIndex is null && part.StartsUnit) AddAudioButton(part.Unit.Id);
                if (_targetIndex is null)
                {
                    var target = part.Atoms.Select(_document.TargetFor).FirstOrDefault(t => t is not null);
                    if (target is not null)
                    {
                        var expand = new Button { Text = T("Expand"), AutomationId = "Reader.Expand." + part.Unit.Id };
                        expand.Clicked += (_, _) => OpenTarget(target); _body.Add(expand);
                    }
                }
                if (part.EndsUnit)
                {
                    var target = _targetIndex is { } i ? _document.Targets[i] : null;
                    var translations = target is not null && target.SegmentId is not null ? _document.Segment(target)!.Translations : part.Unit.Translations;
                    // A unit translation must never be mislabeled as a selected segment's translation.
                    AddTranslation(translations);
                }
            }
            if (_pageIndex + 1 == _pages.Count)
                _body.Add(new Label { Text = _document.Content.Source.AuthorProvider + " · " + _document.Content.Source.LicenseIdentifier + "\n" +
                    (_document.Content.Source.ReviewStatus == ReviewStatus.Approved ? T("Source") : T("Draft")), FontSize = 12 });
            if (_document.Content.Source.SourceId == HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.SourceId)
            {
                _body.Add(new Label { Text = _document.Content.Source.PermissionNotes, FontSize = 12 });
                var source = new Button { Text = _language["Dictionary.About"], AutomationId = "Reader.DictionarySource" };
                source.Clicked += async (_, _) => await Navigation.PushAsync(new DefaultDictionaryPage(_language));
                _body.Add(source);
            }
            foreach (var action in management) _body.Add(action);
        }
        finally { _refreshing = false; }
    }

    private void AddTranslation(IReadOnlyDictionary<string, string> translations)
    {
        var text = UiLanguagePolicy.SelectAuxiliaryTranslation(translations, _language.CurrentLanguage);
        if (text is not null) _body.Add(new Label { Text = text, FontSize = 17 * _scale });
    }
    private void AddAudioButton(Guid targetId)
    {
        var button = new Button { Text = _language["Audio.Manage"], AutomationId = "Reader.Audio" };
        button.Clicked += async (_, _) =>
        {
            if (_opening) return; _opening = true;
            try
            {
                var services = Handler!.MauiContext!.Services;
                await Navigation.PushAsync(new AudioTracksPage(targetId,
                    services.GetRequiredService<HanMate.Infrastructure.Database.LocalAudioStore>(),
                    services.GetRequiredService<HanMate.Core.Audio.PlaybackCoordinator>(),
                    services.GetRequiredService<HanMate.Infrastructure.Database.SqliteContentDocumentStore>(), _language));
            }
            catch { await DisplayAlertAsync(Title, _language["Library.Failed"], _language["Library.Cancel"]); }
            finally { _opening = false; }
        };
        _body.Add(button);
    }
    private async Task ChangeTargetAsync(int index)
    {
        if (_opening || index < 0 || index >= _document.Targets.Count) return;
        _opening = true;
        try
        {
        await StopReadingAsync(); if (!_active) return;
        _targetIndex = index; _pages = _document.PagesFor(_document.Targets[index]); _pageIndex = 0; Render(); _ = _scroll.ScrollToAsync(0, 0, false);
        }
        finally { _opening = false; }
    }
    private async void OpenTarget(ReadingTarget? target)
    {
        if (_opening || target is null || _targetIndex is not null) return;
        var index = _document.Targets.ToList().IndexOf(target); if (index < 0) return;
        _opening = true;
        try { await Navigation.PushAsync(new ReadingPage(_document, _language, index, Math.Min(2, Math.Max(1.5, _scale)), _allowEditing, autoPlay: true)); }
        finally { _opening = false; }
    }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        _language.PropertyChanged -= OnLanguageChanged;
        if (_readerShell is not null) _readerShell.Navigated -= OnShellNavigated;
        _readerShell = null;
        base.OnHandlerChanging(args);
    }
    protected override async void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.MauiContext is { } context)
        {
            _readerShell = Shell.Current;
            if (_readerShell is not null) _readerShell.Navigated += OnShellNavigated;
            _player = context.Services.GetRequiredService<HanMate.Core.Audio.PlaybackCoordinator>();
            if (_active)
            {
                _language.PropertyChanged -= OnLanguageChanged;
                _language.PropertyChanged += OnLanguageChanged;
            }
            Render(); await LoadPreferencesAsync(); await TryAutoPlayAsync();
        }
    }
    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
    {
        // Shell may return to a pushed page via a tab without raising Appearing again.
        if (_readerShell != Shell.Current || Handler?.MauiContext is null) return;
        var current = _readerShell?.CurrentPage == this;
        if (!current)
        {
            if (_active) { _active = false; await StopReadingAsync(); }
            return;
        }
        _active = true;
        _language.PropertyChanged -= OnLanguageChanged;
        _language.PropertyChanged += OnLanguageChanged;
        if (_renderedLanguage != _language.CurrentLanguage) Render();
        await LoadPreferencesAsync();
    }
    private async void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Shell can retain a page/handler from a previous window. Never resolve scoped
        // services or repaint that old visual tree from a singleton language event.
        if (args.PropertyName != nameof(LocalizationService.CurrentLanguage) || !_active || Shell.Current?.CurrentPage != this) return;
        await StopReadingAsync();
        if (_active && Shell.Current?.CurrentPage == this) Render();
    }
}
