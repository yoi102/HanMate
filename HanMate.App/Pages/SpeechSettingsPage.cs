using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Infrastructure.Voices;

namespace HanMate.App.Pages;

public sealed class SpeechSettingsPage : ContentPage
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly PlaybackCoordinator _audio;
    private readonly LocalizationService _language;
    private readonly SpeechVoicePacks _packs;
    private readonly AiVoiceService _ai;
    private readonly Picker _engine = new() { AutomationId = "Speech.Engine" };
    private readonly Picker _voices = new() { AutomationId = "Speech.Voice" };
    private readonly Label _status = new() { AutomationId = "Speech.Status" };
    private readonly Label _hint = new();
    private readonly Button _refresh, _preview, _manage;
    private LocalVoice[] _systemVoices = [];
    private VoiceSpeaker[] _offlineVoices = [];
    private CancellationTokenSource _lifetime = new();
    private bool _visible, _busy, _loading;
    private string T(string key) => _language["Speech." + key];
    public SpeechSettingsPage(PlaybackCoordinator audio, LocalizationService language, SpeechVoicePacks packs, AiVoiceService ai)
    {
        _audio = audio; _language = language; _packs = packs; _ai = ai; Title = T("Title");
        _engine.ItemsSource = new[] { "MeloTTS", "Kokoro", T("System") };
        _refresh = new() { Text = T("Refresh"), AutomationId = "Speech.Refresh" };
        _preview = new() { Text = T("Preview"), AutomationId = "Speech.Preview" };
        _manage = new() { Text = T("ManagePack"), AutomationId = "Speech.ManagePack" };
        var stop = new Button { Text = language["Audio.Stop"], AutomationId = "Speech.Stop" };
        _engine.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || _engine.SelectedIndex < 0) return;
            await RunAsync(async token =>
            {
                var choice = (SpeechEngine)_engine.SelectedIndex;
                if (choice != SpeechEngine.System)
                {
                    var pack = _packs.Get(choice); var state = await pack.StateAsync(token);
                    if (state.Installed) await pack.SelectAsync(state.Speaker ?? pack.Pack.DefaultSpeaker, token);
                }
                token.ThrowIfCancellationRequested(); SpeechPreferences.Engine = choice;
                await LoadVoicesAsync(token);
            });
        };
        _voices.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || _voices.SelectedIndex < 0) return;
            var index = _voices.SelectedIndex;
            await RunAsync(async token =>
            {
                if (SpeechPreferences.Engine == SpeechEngine.System) SpeechPreferences.SystemVoiceId = _systemVoices[index].Id;
                else await _packs.Get(SpeechPreferences.Engine).SelectAsync(_offlineVoices[index].Id, token);
                _status.Text = _language["State.Saved"];
            });
        };
        _refresh.Clicked += async (_, _) => await RunAsync(LoadVoicesAsync);
        _preview.Clicked += async (_, _) => await RunAsync(async token =>
        {
            _status.Text = _language["Audio.Playing"];
            var speech = new TextSpeechService(_ai);
            var result = await _audio.PlayOperationAsync(_owner, "speech-preview", ct => speech.SpeakAsync(NativeSpeech.Sample, null, ct));
            token.ThrowIfCancellationRequested();
            _status.Text = _language["Audio." + (result switch
            { PlaybackOutcome.Completed => "Played", PlaybackOutcome.Cancelled => "Stopped", PlaybackOutcome.Busy => "Busy", _ => "NoPlayback" })];
        });
        stop.Clicked += async (_, _) => await _audio.StopAsync(_owner);
        _manage.Clicked += async (_, _) =>
        {
            if (_busy || SpeechPreferences.Engine == SpeechEngine.System) return;
            var choice = SpeechPreferences.Engine;
            await Navigation.PushAsync(new VoicePacksPage(_language, _packs.Get(choice), _ai, _audio, choice));
        };
        SemanticProperties.SetDescription(_engine, T("Engine"));
        SemanticProperties.SetDescription(_voices, T("Voice"));
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children =
        { new Label { Text = T("Engine") }, _engine, _hint, new Label { Text = T("Voice") }, _voices,
          _status, _refresh, new Label { Text = NativeSpeech.Sample }, _preview, stop, _manage } } };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); _visible = true;
        if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new(); }
        await RunAsync(LoadVoicesAsync);
    }
    protected override async void OnDisappearing()
    { _visible = false; _lifetime.Cancel(); base.OnDisappearing(); await _audio.StopAsync(_owner); }
    private async Task LoadVoicesAsync(CancellationToken token)
    {
        _loading = true;
        _status.Text = _language["DictionaryDetail.SpeechPending"];
        try
        {
            var engine = SpeechPreferences.Engine; _engine.SelectedIndex = (int)engine;
            _hint.Text = T(engine == SpeechEngine.System ? "SystemHint" : engine == SpeechEngine.Melo ? "MeloHint" : "KokoroHint");
            _refresh.IsVisible = engine == SpeechEngine.System; _manage.IsVisible = engine != SpeechEngine.System;
            if (engine == SpeechEngine.System)
            {
                _systemVoices = (await NativeSpeech.ListAsync(token)).ToArray(); token.ThrowIfCancellationRequested();
                _voices.ItemsSource = _systemVoices.Select(v => v.ToString()).ToArray();
                var index = Array.FindIndex(_systemVoices, v => v.Id == SpeechPreferences.SystemVoiceId);
                if (index < 0) index = Array.FindIndex(_systemVoices, v => v.Name == SpeechPreferences.SystemVoiceId);
                _voices.SelectedIndex = index >= 0 ? index : SpeechPreferences.SystemVoiceId.Length == 0 && _systemVoices.Length > 0 ? 0 : -1;
                if (_voices.SelectedIndex >= 0) SpeechPreferences.SystemVoiceId = _systemVoices[_voices.SelectedIndex].Id;
                _status.Text = T(_voices.SelectedIndex >= 0 ? "Available" : "Unavailable");
            }
            else
            {
                var pack = _packs.Get(engine); var state = await pack.StateAsync(token); token.ThrowIfCancellationRequested();
                _offlineVoices = pack.Pack.Voices ?? [];
                _voices.ItemsSource = _offlineVoices.Select(v => _language["Voice." + (v.Gender == "male" ? "Male" : "Female")] + " · " + v.Name).ToArray();
                _voices.SelectedIndex = Array.FindIndex(_offlineVoices, v => v.Id == state.Speaker);
                _status.Text = _language["Voice." + (state.Speaker is not null ? "Selected" : state.Installed ? "Disabled" : "NotInstalled")];
            }
        }
        finally { _loading = false; }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy || !_visible) return; _busy = true;
        _engine.IsEnabled = _voices.IsEnabled = _refresh.IsEnabled = _preview.IsEnabled = _manage.IsEnabled = false;
        try { await _audio.StopAsync(_owner); await action(_lifetime.Token); }
        catch (OperationCanceledException) { }
        catch
        {
            if (_visible)
            {
                _loading = true; _engine.SelectedIndex = (int)SpeechPreferences.Engine; _loading = false;
                _status.Text = _language["Audio.Failed"];
            }
        }
        finally
        {
            _busy = false;
            _engine.IsEnabled = _voices.IsEnabled = _refresh.IsEnabled = _manage.IsEnabled = true;
            _preview.IsEnabled = _voices.SelectedIndex >= 0;
        }
    }
}
