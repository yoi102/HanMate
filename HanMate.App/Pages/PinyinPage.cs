using HanMate.Infrastructure.Database;
using System.ComponentModel;
using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Pinyin;

namespace HanMate.App.Pages;

public sealed class PinyinPage : ContentPage
{
    private readonly LocalizationService _language;
    private readonly TeachingCatalogStore _teaching;
    private readonly BundledPronunciationService _source;
    private readonly PlaybackCoordinator _playback;
    private readonly PinyinSpeechService _speech;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 18 };
    private readonly Grid _layout = new();
    private readonly ScrollView _scroll;
    private PinyinGuideView? _guide;
    private PinyinGuideSession? _tour;
    private PinyinTeachingItem? _guideItem;
    private PinyinButton? _guideTarget;
    private bool _guideOpening;
    private PinyinCourse? _course;
    private bool _opening, _active;
    private bool _dictionaryPrepared;
    private long _generation;

    public PinyinPage(LocalizationService language, BundledPronunciationService source, PlaybackCoordinator playback, TeachingCatalogStore teaching, PinyinSpeechService speech)
    {
        _language = language; _source = source; _playback = playback; _teaching = teaching; _speech = speech;
        Shell.SetNavBarIsVisible(this, false);
        Title = T("Title"); _scroll = new ScrollView { Content = _body }; _layout.Add(_scroll); Content = _layout;
        Loaded += (_, _) => PrepareDictionary();
    }
    private string T(string key) => _language["Pinyin." + key];

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true; _language.PropertyChanged += OnLanguageChanged;
        try
        {
            _course = await _teaching.LoadAsync(await _source.GetCourseAsync());
            if (_active)
            {
                Render();
                PrepareDictionary();
                if (_tour?.IsActive == true || !PinyinGuideSession.HasSeen) await ShowGuideAsync();
            }
        }
        catch { _body.Clear(); _body.Add(new Label { Text = T("LoadFailed") }); }
    }
    private void PrepareDictionary()
    {
        if (!_active || _dictionaryPrepared || _course is null || Handler?.MauiContext?.Services.GetService<OfflineSearchStore>() is not { } dictionary) return;
        _dictionaryPrepared = true;
        var first = _course.Data.Items.SelectMany(item => Enumerable.Range(1, 4).SelectMany(tone => _course.ExamplesForTone(item, tone))).FirstOrDefault();
        _ = DictionaryEntryPage.PrepareAsync(dictionary, first is null ? null : _course.Unit(first));
    }
    protected override async void OnDisappearing()
    {
        _active = false; _generation++; _language.PropertyChanged -= OnLanguageChanged;
        HideGuide();
        await _playback.StopAsync(_owner); base.OnDisappearing();
    }
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.CurrentLanguage) || _course is null) return;
        var resume = _guide is not null; HideGuide(); Render();
        if (resume) _ = ShowGuideAsync();
    }

    private void Render()
    {
        Title = T("Title"); _body.Clear(); _guideTarget = null;
        if (_course is null) return;
        _guideItem = _course.Data.Items.FirstOrDefault(i => i.Group == "initial" && i.Display == "b") ?? _course.Data.Items.FirstOrDefault();
        var first = true;
        foreach (var group in _course.Data.Items.GroupBy(i => i.Group))
        {
            var heading = new Label { Text = T("Group." + group.Key), FontSize = 23, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
            if (first)
            {
                first = false;
                var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
                var help = new Button { Text = "?", FontSize = 21, FontAttributes = FontAttributes.Bold,
                    WidthRequest = 44, HeightRequest = 44, Padding = 0, CornerRadius = 22,
                    TextColor = Color.FromArgb("#512BD4"), BackgroundColor = Color.FromArgb("#EEE6FA"), AutomationId = "Pinyin.Guide.Open" };
                SemanticProperties.SetDescription(help, T("Guide.Title")); ToolTipProperties.SetText(help, T("Guide.Title"));
                help.Clicked += async (_, _) => { _tour = new(); await ShowGuideAsync(); };
                header.Add(heading); header.Add(help, 1); _body.Add(header);
            }
            else _body.Add(heading);
            var wrap = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var item in group)
            {
                var button = new PinyinButton { Text = item.Display, FontSize = 25, MinimumWidthRequest = 78, MinimumHeightRequest = 62, Margin = new Thickness(3), AutomationId = "Pinyin.Item." + item.Group + "." + item.Display };
                SemanticProperties.SetDescription(button, item.Display + ". " + T("Hint"));
                if (item == _guideItem) _guideTarget = button;
                button.Clicked += async (_, _) =>
                {
                    if (!button.ConsumeClick() || (_guide is not null && item != _guideItem)) return;
                    var tour = _tour; var result = await PlayDemoAsync(item);
                    if (_active && _guide is not null && _tour == tour && result == PlaybackOutcome.Completed)
                    { tour?.Advance(1); _guide.SetStep(tour!.Step); }
                };
                button.ShowExamples += async (_, _) =>
                {
                    if (_guide is not null)
                    {
                        if (item != _guideItem || _tour?.Step != 2) return;
                        _tour.Advance(2); HideGuide();
                        await OpenExamplesAsync(item, _tour);
                    }
                    else await OpenExamplesAsync(item);
                };
                wrap.Add(button);
            }
            _body.Add(wrap);
        }
    }

    private async Task ShowGuideAsync()
    {
        if (!_active || _guide is not null || _guideOpening || _opening || _guideItem is null || _guideTarget is null) return;
        _guideOpening = true;
        try
        {
            await _playback.StopAsync(_owner);
            await _scroll.ScrollToAsync(0, 0, false);
            if (!_active || _guide is not null) return;
            if (_tour?.IsActive != true) _tour = new();
            _tour.ReturnToPinyin();
            _scroll.Orientation = ScrollOrientation.Neither;
            _guide = new(_guideTarget, _language, _guideItem.Display, _tour.Step, () => _ = CloseGuideAsync());
            _guide.Show(this, _layout);
        }
        finally { _guideOpening = false; }
    }
    private void HideGuide()
    {
        _guide?.Dispose(); _guide = null; _scroll.Orientation = ScrollOrientation.Vertical;
    }
    private async Task CloseGuideAsync() { _tour?.Finish(); HideGuide(); await _playback.StopAsync(_owner); }
    protected override bool OnBackButtonPressed()
    {
        if (_guide is null) return base.OnBackButtonPressed();
        _ = CloseGuideAsync(); return true;
    }

    private async Task<PlaybackOutcome?> PlayDemoAsync(PinyinTeachingItem item)
    {
        if (!_active || _course is null) return null;
        var key = _course.DemoPlaybackKey(item);
        var generation = ++_generation;
        var result = await _speech.PlayAsync(_owner, item.Id.ToString(), () => PinyinVoiceInput.Demo(item), key);
        if (_active && generation == _generation && result is null)
            await DisplayAlertAsync(item.Display, T("NoAudio"), _language["Library.Cancel"]);
        if (_active && generation == _generation && result is PlaybackOutcome.Failed or PlaybackOutcome.Busy)
            await DisplayAlertAsync(item.Display, T(result == PlaybackOutcome.Busy ? "Busy" : "PlayFailed"), _language["Library.Cancel"]);
        return result;
    }

    private async Task OpenExamplesAsync(PinyinTeachingItem item, PinyinGuideSession? tour = null)
    {
        if (_opening || !_active || _course is null) return;
        _opening = true;
        try { await _playback.StopAsync(_owner); await Navigation.PushAsync(new PinyinExamplesPage(item, _course, _language, _playback, _speech, tour)); }
        finally { _opening = false; }
    }

}
