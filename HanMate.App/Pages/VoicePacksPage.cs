using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Infrastructure.Voices;

namespace HanMate.App.Pages;

public sealed class VoicePacksPage : ContentPage
{
    private readonly VoicePackStore _packs;
    private readonly AiVoiceService _voice;
    private readonly PlaybackCoordinator _audio;
    private readonly LocalizationService _language;
    private readonly SpeechEngine _engine;
    private readonly VoiceSpeaker[] _options;
    private int SelectedSpeaker => _speakers.SelectedIndex >= 0 ? _options[_speakers.SelectedIndex].Id : _packs.Pack.DefaultSpeaker;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Label _status = new() { AutomationId = "Voice.Status" };
    private readonly ProgressBar _progress = new() { IsVisible = false };
    private readonly Picker _speakers = new() { AutomationId = "Voice.Speaker" };
    private readonly Button _download, _preview, _use, _disable, _remove, _cancel;
    private readonly Editor _sample = new() { Text = NativeSpeech.Sample, MaxLength = 160, HeightRequest = 110, AutomationId = "Voice.Sample" };
    private CancellationTokenSource _lifetime = new();
    private bool _active, _busy;
    private long _generation;
    private string T(string key) => _language["Voice." + key];
    public VoicePacksPage(LocalizationService language, VoicePackStore packs, AiVoiceService voice, PlaybackCoordinator audio, SpeechEngine engine = SpeechEngine.Melo)
    {
        _packs = packs; _voice = voice; _audio = audio; _language = language; _engine = engine; Title = packs.Pack.Name;
        _options = packs.Pack.Voices ?? Enumerable.Range(0, packs.Pack.Speakers).Select(i => new VoiceSpeaker(i, (i + 1).ToString(), "")).ToArray();
        _download = Button("Download", async () => await DownloadAsync());
        _preview = Button("Preview", async () => await RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(_sample.Text)) return;
            await _voice.ValidateAsync(_sample.Text, SelectedSpeaker, token, _engine);
            token.ThrowIfCancellationRequested();
            _status.Text = T("Generating");
            var outcome = await _audio.PlayOperationAsync(_owner, "ai-preview", ct => _voice.SpeakAsync(_sample.Text, SelectedSpeaker, ct, _engine));
            token.ThrowIfCancellationRequested();
            _status.Text = _language["Audio." + (outcome switch { PlaybackOutcome.Completed => "Played", PlaybackOutcome.Cancelled => "Stopped", PlaybackOutcome.Busy => "Busy", _ => "Failed" })];
        }));
        _use = Button("Use", async () => await RunAsync(async token => { await _packs.SelectAsync(SelectedSpeaker, token); token.ThrowIfCancellationRequested(); SpeechPreferences.Engine = _engine; _status.Text = T("Selected"); }));
        _disable = Button("Disable", async () => await RunAsync(async token => { await _packs.SelectAsync(null, token); token.ThrowIfCancellationRequested(); _status.Text = T("Disabled"); }));
        _remove = Button("Remove", async () =>
        {
            var generation = _generation;
            if (_busy || !await DisplayAlertAsync(T("Remove"), T("RemoveConfirm"), T("Remove"), _language["Library.Cancel"])) return;
            if (!_active || generation != _generation) return;
            await RunAsync(async token => { await _packs.RemoveAsync(token); token.ThrowIfCancellationRequested(); _status.Text = T("NotInstalled"); });
        });
        _cancel = Button("Cancel", async () => { _lifetime.Cancel(); await _audio.StopAsync(_owner); }); _cancel.IsVisible = false;
        _speakers.ItemsSource = _options.Select(v => (v.Gender.Length > 0 ? T(v.Gender == "female" ? "Female" : "Male") + " · " : "") + v.Name).ToList();
        _speakers.SelectedIndex = Array.FindIndex(_options, v => v.Id == packs.Pack.DefaultSpeaker);
        SemanticProperties.SetDescription(_speakers, T("ChooseSpeaker"));
        SemanticProperties.SetDescription(_sample, T("Sample"));
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 18, Spacing = 12, Children =
        { new Label { Text = T("Hint") }, new Label { Text = string.Format(T("Info"), packs.Pack.Bytes / 1048576d, _options.Length) },
          _status, _progress, _download, new Label { Text = T("ChooseSpeaker") }, _speakers,
          new Label { Text = T("Sample") }, _sample, _preview, _use, _disable, _remove, _cancel,
          new Label { Text = T("Privacy") }, new Label { Text = T("License") + "\n" + packs.Pack.Name + " · " + packs.Pack.License + "\n" + packs.Pack.Source } } } };
    }
    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = T(key), AutomationId = "Voice." + key };
        button.Clicked += async (_, _) => await action(); return button;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true; _generation++;
        await RunAsync(async token =>
        {
            var state = await _packs.StateAsync(token);
            token.ThrowIfCancellationRequested();
            if (state.Speaker is { } speaker) _speakers.SelectedIndex = Array.FindIndex(_options, v => v.Id == speaker);
            _status.Text = !OfflineVoiceSynthesizer.Supported ? T("Unsupported") : T(state.Speaker is not null ? "Selected" : state.Installed ? "Installed" : "NotInstalled");
        });
    }
    protected override async void OnDisappearing()
    { _active = false; _generation++; _lifetime.Cancel(); base.OnDisappearing(); await _audio.StopAsync(_owner); }
    private async Task DownloadAsync()
    {
        var generation = _generation;
        if (_busy) return;
        if (!_packs.HasBundledFiles && !await DisplayAlertAsync(T("Download"), string.Format(T("DownloadConfirm"), _packs.Pack.Bytes / 1048576d), T("Download"), _language["Library.Cancel"])) return;
        if (!_active || generation != _generation) return;
        await RunAsync(async token =>
        {
            var reporting = true;
            _progress.Progress = 0; _progress.IsVisible = true; _status.Text = string.Format(T("Progress"), 0d);
            try
            {
                await _packs.InstallAsync(new Progress<double>(p =>
                {
                    if (reporting && !token.IsCancellationRequested && _active && generation == _generation)
                    { _progress.Progress = p; _status.Text = string.Format(T("Progress"), p); }
                }), token);
            }
            finally { reporting = false; }
            token.ThrowIfCancellationRequested();
            _status.Text = T("Installed");
        });
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy || !_active) return; _busy = true;
        var generation = _generation;
        if (_lifetime.IsCancellationRequested && _active) { _lifetime.Dispose(); _lifetime = new(); }
        foreach (var button in new[] { _download, _preview, _use, _disable, _remove }) button.IsEnabled = false;
        _speakers.IsEnabled = _sample.IsEnabled = false; _cancel.IsVisible = true;
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) { if (_active && generation == _generation) _status.Text = _language["Audio.Stopped"]; }
        catch (UnsupportedVoiceTextException) { if (_active && generation == _generation) _status.Text = T("UnsupportedText"); }
        catch (TimeoutException) { if (_active && generation == _generation) _status.Text = T("DownloadTimeout"); }
        catch { if (_active && generation == _generation) _status.Text = T("Failed"); }
        finally
        {
            _cancel.IsVisible = _progress.IsVisible = false;
            if (_active)
            {
                VoicePackState state;
                try { state = await _packs.StateAsync(); } catch { state = new(false, null); }
                if (_active)
                {
                    var supported = OfflineVoiceSynthesizer.Supported;
                    _download.IsEnabled = supported && !state.Installed;
                    _speakers.IsEnabled = _sample.IsEnabled = _preview.IsEnabled = _use.IsEnabled = supported && state.Installed;
                    _disable.IsEnabled = state.Speaker is not null; _remove.IsEnabled = state.HasFiles;
                    if (generation != _generation)
                    {
                        if (state.Speaker is { } speaker) _speakers.SelectedIndex = Array.FindIndex(_options, v => v.Id == speaker);
                        _status.Text = !supported ? T("Unsupported") : T(state.Speaker is not null ? "Selected" : state.Installed ? "Installed" : "NotInstalled");
                    }
                }
            }
            _busy = false;
        }
    }
}
