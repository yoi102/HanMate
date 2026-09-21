using System.ComponentModel;
using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Pinyin;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class PinyinExamplesPage : ContentPage
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly PlaybackCoordinator _playback;
    private readonly PinyinSpeechService _speech;
    private readonly PinyinTeachingItem _item;
    private readonly PinyinCourse _course;
    private readonly LocalizationService _language;
    private readonly List<PinyinExampleView> _cards = [];
    private readonly Grid _layout = new();
    private readonly ScrollView _scroll = new();
    private readonly PinyinGuideSession? _tour;
    private PinyinGuideView? _guide;
    private PinyinExampleView? _guideCard;
    private VisualElement? _guideTarget;
    private PinyinExample? _guideExample;
    private bool _active, _opening;
    private long _generation;
    private string? _renderedLanguage;
    private string T(string key) => _language["Pinyin." + key];
    public PinyinExamplesPage(PinyinTeachingItem item, PinyinCourse course, LocalizationService language, PlaybackCoordinator playback, PinyinSpeechService speech, PinyinGuideSession? guideSession = null)
    {
        _item = item; _course = course; _language = language; _playback = playback; _speech = speech; _tour = guideSession;
        _layout.Add(_scroll); Content = _layout; Loaded += (_, _) => ShowGuide(); Render();
    }

    private void Highlight(PinyinExampleView? selected)
    { foreach (var card in _cards) card.BackgroundColor = card == selected ? Color.FromArgb("#E6DEF7") : Colors.Transparent; }
    private void Render()
    {
        HideGuide(); _generation++; _cards.Clear(); _renderedLanguage = _language.CurrentCultureName;
        _guideCard = null; _guideTarget = null; _guideExample = null;
        Title = _item.Display + " · " + T("Examples");
        var body = new VerticalStackLayout { Padding = 18, Spacing = 20 };
        for (var tone = 1; tone <= 4; tone++)
        {
            var examples = _course.ExamplesForTone(_item, tone);
            if (examples.Count == 0) continue;
            var group = new VerticalStackLayout { Spacing = 8 };
            group.Add(new Label { Text = T("Tone" + tone), FontAttributes = FontAttributes.Bold, FontSize = 18 });
            foreach (var example in examples)
            {
                var unit = _course.Unit(example);
                var text = new VerticalStackLayout { Padding = 10, Spacing = 4, InputTransparent = true, CascadeInputTransparent = true };
                var ruby = new RubyWordView(unit, example) { InputTransparent = true, CascadeInputTransparent = true };
                text.Add(ruby);
                var translation = UiLanguagePolicy.SelectAuxiliaryTranslation(unit.Translations, _language.CurrentLanguage);
                if (translation is not null) text.Add(new Label { Text = translation, FontSize = 16, InputTransparent = true });
                var card = new PinyinExampleView { Content = text, AutomationId = "Pinyin.Example." + unit.Id };
                SemanticProperties.SetDescription(card, unit.Text + " " + string.Join(" ", unit.Tokens.Select(t => t.Pinyin?.Display)));
                SemanticProperties.SetHint(card, T("ExampleGesture"));
                if (_guideCard is null && unit.Text.Length == 1 && ruby.Children.FirstOrDefault() is VisualElement cell)
                {
                    _guideCard = card; _guideExample = example; _guideTarget = cell;
                    cell.Loaded += (_, _) => ShowGuide(); cell.SizeChanged += (_, _) => ShowGuide();
                }
                card.Read += async (_, _) =>
                {
                    if (_guide is not null && card != _guideCard) return;
                    var result = await PlayAsync(example, card);
                    if (_active && _guide is not null && result == PlaybackOutcome.Completed)
                    { _tour?.Advance(3); _guide.SetStep(_tour!.Step); }
                };
                card.Define += async (_, _) =>
                {
                    if (_guide is not null)
                    {
                        if (card != _guideCard || _tour?.Step != 4) return;
                        HideGuide();
                        if (await OpenDictionaryAsync(example)) _tour.Finish();
                        else if (_active) ShowGuide();
                    }
                    else await OpenDictionaryAsync(example);
                };
                _cards.Add(card); group.Add(card);
            }
            body.Add(group);
        }
        _scroll.Content = body;
    }
    private async Task<PlaybackOutcome?> PlayAsync(PinyinExample example, PinyinExampleView card)
    {
        if (!_active || _opening) return null;
        var generation = ++_generation; Highlight(card);
        var key = _course.PlaybackKey(example);
        var result = await _speech.PlayExampleAsync(_owner, _course.Unit(example), key);
        if (!_active || generation != _generation) return result;
        Highlight(null);
        if (result is null) await DisplayAlertAsync(_course.Unit(example).Text, T("NoAudio"), _language["Library.Cancel"]);
        if (result is PlaybackOutcome.Failed or PlaybackOutcome.Busy)
            await DisplayAlertAsync(_course.Unit(example).Text, T(result == PlaybackOutcome.Busy ? "Busy" : "PlayFailed"), _language["Library.Cancel"]);
        return result;
    }
    private async Task<bool> OpenDictionaryAsync(PinyinExample example)
    {
        if (!_active || _opening) return false;
        _opening = true; var generation = ++_generation; Highlight(null);
        try
        {
            await _playback.StopAsync(_owner);
            var unit = _course.Unit(example); ContentDocument? found = null; var allowEditing = false;
            if (Handler?.MauiContext?.Services.GetRequiredService<OfflineSearchStore>() is { } search)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var match = await Task.Run(() => search.FindEntryAsync(unit));
                found = match.Document; allowEditing = found is not null && !match.IsReadOnly;
                if (await Task.Run(() => search.GetEpochAsync()) != match.Epoch) found = null;
                SpeechDiagnostics.Write($"dictionary lookup_ms={timer.ElapsedMilliseconds} matched={found is not null}");
            }
            if (!_active || generation != _generation) return false;
            // Same detail view used by Search. Bundled draft explanations remain read-only;
            // they do not masquerade as an installed dictionary or create database rows.
            if (Handler?.MauiContext?.Services is not { } services) return false;
            await Navigation.PushAsync(DictionaryEntryPage.Create(found ?? _course.Content(example), _language, found is not null && allowEditing, services));
            return true;
        }
        catch { if (_active) await DisplayAlertAsync(Title, _language["Search.Failed"], _language["Library.Cancel"]); return false; }
        finally { _opening = false; }
    }
    protected override void OnAppearing()
    {
        base.OnAppearing(); _active = true; _language.PropertyChanged += OnLanguageChanged;
        if (_renderedLanguage != _language.CurrentCultureName) Render();
        Dispatcher.Dispatch(ShowGuide);
    }
    protected override async void OnDisappearing()
    { _active = false; HideGuide(); _generation++; _language.PropertyChanged -= OnLanguageChanged; Highlight(null); await _playback.StopAsync(_owner); base.OnDisappearing(); }
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(LocalizationService.CurrentLanguage)) { Render(); Dispatcher.Dispatch(ShowGuide); } }

    private void ShowGuide()
    {
        if (!_active || _guide is not null || _tour?.IsActive != true || _guideTarget is null || _guideExample is null ||
            Handler?.MauiContext is null || _guideTarget.Handler is null || _guideTarget.Width <= 0 || _guideTarget.Height <= 0) return;
        _scroll.Orientation = ScrollOrientation.Neither;
        _guide = new(_guideTarget, _language, _course.Unit(_guideExample).Text, _tour.Step, () => _ = CloseGuideAsync());
        _guide.Show(this, _layout);
    }
    private void HideGuide() { _guide?.Dispose(); _guide = null; _scroll.Orientation = ScrollOrientation.Vertical; }
    private async Task CloseGuideAsync() { _tour?.Finish(); HideGuide(); await _playback.StopAsync(_owner); }
    protected override bool OnBackButtonPressed()
    {
        if (_guide is null) return base.OnBackButtonPressed();
        _ = CloseGuideAsync(); return true;
    }
}
