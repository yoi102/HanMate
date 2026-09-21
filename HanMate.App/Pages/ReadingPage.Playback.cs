using HanMate.App.Controls;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed partial class ReadingPage
{
    private readonly Guid _playOwner = Guid.NewGuid();
    private readonly Button _playReading = new() { AutomationId = "Reader.Play", FontSize = 14 };
    private readonly Button _stopReading = new() { AutomationId = "Reader.Stop", FontSize = 14 };
    private readonly Button _speechReading = new() { AutomationId = "Reader.Speech", FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _playbackStatus = new() { AutomationId = "Reader.PlaybackStatus", FontSize = 14 };
    private readonly List<RubyTextView> _audioViews = [];
    private bool _active, _autoPlay;
    private long _playGeneration;
    private Guid? _playingTarget;
    // Cache while attached; stop callbacks can run after the window service scope was disposed.
    private PlaybackCoordinator? _player;
    private PlaybackCoordinator? Player => _player;

    private View CreatePlaybackControls()
    {
        var actions = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        actions.Add(_playReading, 0); actions.Add(_stopReading, 1);
        _playReading.Clicked += async (_, _) => await PlayReadingAsync(_targetIndex is { } i ? _document.Targets[i] : null);
        _stopReading.Clicked += async (_, _) => await StopReadingAsync();
        _speechReading.Clicked += async (_, _) => await Navigation.PushAsync(ActivatorUtilities.CreateInstance<SpeechSettingsPage>(Handler!.MauiContext!.Services));
        return new VerticalStackLayout { Padding = new Thickness(12, 0, 12, 6), Spacing = 3,
            IsVisible = _allowEditing, Children = { actions, _speechReading, _playbackStatus } };
    }
    private void RefreshPlaybackControls()
    {
        _playReading.Text = T(_targetIndex is null ? "PlayAll" : "PlaySelection");
        _stopReading.Text = _language["Audio.Stop"];
        _speechReading.Text = _language["Speech.Title"];
        _playReading.IsEnabled = _document.Targets.Count > 0;
        if (string.IsNullOrEmpty(_playbackStatus.Text)) _playbackStatus.Text = T("LocalPlayback");
    }
    private async void ActivateTarget(ReadingTarget? target)
    {
        if (!_active || target is null) return;
        if (!_allowEditing) { if (_targetIndex is null) OpenTarget(target); return; }
        if (_targetIndex is not null || _document.Content.Kind is ContentKind.Word or ContentKind.Grammar)
            await PlayReadingAsync(target);
        else OpenTarget(target);
    }
    private async Task TryAutoPlayAsync()
    {
        // MAUI may raise Appearing before its service context is attached, or while preferences are still loading.
        if (!_autoPlay || !_active || _loadingPreferences || Player is null) return;
        _autoPlay = false;
        await PlayReadingAsync(_targetIndex is { } i ? _document.Targets[i] : null);
    }
    private void HighlightAudio(Guid? target)
    {
        _playingTarget = target;
        foreach (var view in _audioViews) view.SetPlayingTarget(target);
    }
    private async Task StopReadingAsync()
    {
        var generation = ++_playGeneration; HighlightAudio(null);
        if (Player is { } player) await player.StopAsync(_playOwner);
        if (generation == _playGeneration) _playbackStatus.Text = _language["Audio.Stopped"];
    }
    private async Task PlayReadingAsync(ReadingTarget? selected, bool allowSpeech = false)
    {
        if (!_active || !_allowEditing || Player is not { } player) return;
        var generation = ++_playGeneration; HighlightAudio(null);
        _playbackStatus.Text = T("PreparingAudio");
        var store = Handler!.MauiContext!.Services.GetRequiredService<ReadingAudioStore>();
        ReadingAudioPlan? plan = null; string? problem = null;
        var outcome = await player.PlaySequenceAsync(_playOwner,
            $"reading:{_document.Content.Id}:{selected?.SegmentId ?? selected?.UnitId ?? Guid.Empty}:{allowSpeech}", async token =>
            {
                try
                {
                    // The explicit system-voice command keeps its own consent and provider semantics.
                    var engine = Audio.SpeechPreferences.Engine;
                    var systemDefault = engine == Audio.SpeechEngine.System;
                    plan = await Task.Run(() => store.PlanAsync(_document, selected, token, allowSpeechFallback: true), token);
                    if (_document.Content.Kind == ContentKind.Word)
                    {
                        var catalog = await Handler!.MauiContext!.Services.GetRequiredService<Audio.WordSpeechService>().GetCatalogAsync().WaitAsync(token);
                        plan = plan.WithWordRecordings(_document, catalog);
                    }
                    var needsSpeech = plan.Steps.Any(s => s.AssetKey.StartsWith("speech:", StringComparison.Ordinal));
                    var aiSpeaker = !needsSpeech || allowSpeech || systemDefault ? null : await Handler!.MauiContext!.Services.GetRequiredService<Audio.AiVoiceService>().SelectedAsync(token, engine);
                    Audio.LocalVoice? voice = null;
                    if (plan.MissingTargets.Count != 0 && aiSpeaker is null)
                    {
                        if (systemDefault)
                            voice = await Audio.TextSpeechService.SystemVoiceAsync(token);
                        else
                        {
                        if (!allowSpeech) { problem = string.Format(T("MissingAudio"), plan.MissingTargets.Count); throw new InvalidDataException("The reading queue has missing audio."); }
                        if (!await Handler!.MauiContext!.Services.GetRequiredService<SpeechPolicyStore>().IsAllowedAsync(token))
                        { problem = T("SpeechDisabled"); throw new InvalidDataException("System speech is disabled."); }
                        var confirmed = await MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync(T("SpeechFallback"), string.Format(T("SpeechConsent"), plan.MissingTargets.Count), T("SpeechFallback"), _language["Library.Cancel"]));
                        token.ThrowIfCancellationRequested(); if (!confirmed) throw new OperationCanceledException();
                        var voices = await Audio.NativeSpeech.ListAsync(token);
                        if (voices.Count == 0) { problem = _language["Speech.Unavailable"]; throw new InvalidDataException("No installed Mandarin voice."); }
                        var choices = voices.Select((v, i) => $"{i + 1}. {v}").ToArray();
                        var choice = await MainThread.InvokeOnMainThreadAsync(() => DisplayActionSheetAsync(_language["Speech.Voice"], _language["Library.Cancel"], null, choices));
                        token.ThrowIfCancellationRequested(); var index = Array.IndexOf(choices, choice); if (index < 0) throw new OperationCanceledException();
                        voice = voices[index];
                        }
                    }
                    if (plan.Steps.Count == 0) { problem = T("Empty"); throw new InvalidDataException("No speech targets."); }
                    if (aiSpeaker is { } selectedSpeaker)
                    {
                        var ai = Handler!.MauiContext!.Services.GetRequiredService<Audio.AiVoiceService>();
                        foreach (var step in plan.Steps.Where(s => s.AssetKey.StartsWith("speech:", StringComparison.Ordinal)))
                            await ai.ValidateAsync(step.Text, selectedSpeaker, token, engine);
                    }
                    return plan.Steps.Select(s => s.AssetKey.StartsWith("speech:", StringComparison.Ordinal)
                        ? aiSpeaker is { } sid ? $"ai:{engine}:{sid}:{s.AssetKey}" : $"tts:{Uri.EscapeDataString(voice!.Id)}:{s.AssetKey}" : s.AssetKey).ToArray();
                }
                catch (UnsupportedVoiceTextException) { problem = _language["Voice.UnsupportedText"]; throw; }
                catch (Audio.SpeechUnavailableException) { problem = _language["Speech.Unavailable"]; throw; }
                catch (InvalidOperationException) { problem = T("StaleAudio"); throw; }
            }, index => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_active || generation != _playGeneration || plan is null) return;
                var step = plan.Steps[index]; HighlightAudio(step.TargetId);
                _playbackStatus.Text = string.Format(T(step.TargetId == step.UnitId ? "PlayingUnit" : "PlayingSegment"), index + 1, plan.Steps.Count);
            }));
        if (!_active || generation != _playGeneration) return;
        _playGeneration++; // Ignore progress callbacks queued just before completion or background cancellation.
        HighlightAudio(null);
        _playbackStatus.Text = outcome switch
        {
            PlaybackOutcome.Completed => _language["Audio.Played"],
            PlaybackOutcome.Cancelled => _language["Audio.Stopped"],
            PlaybackOutcome.Busy => _language["Audio.Busy"],
            _ => problem ?? T("PlaybackFailed")
        };
    }
}
