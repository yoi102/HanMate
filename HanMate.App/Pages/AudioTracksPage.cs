using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class AudioTracksPage(Guid targetId, LocalAudioStore store, PlaybackCoordinator audio,
    SqliteContentDocumentStore contentStore, LocalizationService language) : ContentPage
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly List<Button> _actions = [];
    private Label _status = new();
    private AudioTarget? _target;
    private bool _busy, _visible, _permissionDenied;
    private long _playVersion;
    private string T(string key) => language["Audio." + key];

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _visible = true;
        await RunAsync(ReloadAsync, false);
    }
    protected override async void OnDisappearing()
    {
        _visible = false; _playVersion++; base.OnDisappearing(); await audio.StopAsync(_owner);
    }

    private Button Action(string key, Func<Task> action, bool reload = true)
    {
        var button = new Button { Text = T(key), AutomationId = "Audio." + key };
        button.Clicked += async (_, _) => await RunAsync(action, reload);
        _actions.Add(button); return button;
    }
    private async Task RunAsync(Func<Task> action, bool reload = true)
    {
        if (_busy) return;
        _busy = true; foreach (var button in _actions) button.IsEnabled = false;
        string? error = null;
        try
        {
            if (audio.IsRecording) { error = T("Busy"); return; }
            await action();
        }
        catch (UnauthorizedAccessException) { error = T("PermissionDenied"); }
        catch (OperationCanceledException) { error = T("Stopped"); }
        catch (Files.FileSaveException) { error = language["Transfer.SaveFailed"]; }
        catch (InvalidDataException) { error = T("InvalidFile"); }
        catch (Exception ex)
        {
            // Do not log the exception message, which can contain user text or paths.
            Console.Error.WriteLine($"Audio operation failed: {ex.GetType().Name} 0x{ex.HResult:X8}\n{ex.StackTrace}");
            error = T("Failed");
        }
        finally
        {
            try { if (reload && _visible) await ReloadAsync(); }
            catch { error ??= T("Failed"); }
            _busy = false; foreach (var button in _actions) button.IsEnabled = true;
            if (error is not null) _status.Text = error;
        }
    }

    private async Task ReloadAsync()
    {
        var target = await store.GetTargetAsync(targetId);
        var snapshot = await contentStore.GetAsync(target.ContentId) ?? throw new InvalidOperationException("Missing content.");
        if (target != await store.GetTargetAsync(targetId)) throw new InvalidOperationException("Target changed.");
        _target = target;
        var document = new ReadingDocument(snapshot.Document);
        var unit = snapshot.Document.TextUnits.First(u => u.Id == targetId || u.Segments.Any(s => s.Id == targetId));
        var selected = new ReadingTarget(unit.Id, unit.Id == targetId ? null : targetId);
        var pages = selected.SegmentId is null
            ? document.Pages.SelectMany(p => p.Parts).Where(p => p.Unit.Id == unit.Id)
            : document.PagesFor(selected).SelectMany(p => p.Parts);
        var tracks = await store.ListTracksAsync(targetId); var drafts = await store.ListDraftsAsync(targetId);
        _actions.Clear(); _status = new Label { AutomationId = "Audio.Status" };
        Title = T("Manage");
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = snapshot.Document.Title, FontSize = 24, FontAttributes = FontAttributes.Bold });
        body.Add(new Label { Text = T(selected.SegmentId is null ? "WholeUnit" : "Segment"), FontAttributes = FontAttributes.Bold });
        var atoms = pages.SelectMany(p => p.Atoms).ToArray();
        var atomPages = atoms.Chunk(ReadingDocument.PageAtomLimit).ToArray();
        var preview = new ScrollView { HeightRequest = 180, Content = new RubyTextView(atomPages.FirstOrDefault() ?? [], true, 1, null) };
        if (atomPages.Length > 1)
        {
            var paging = new Picker { Title = language["Reader.Page"], ItemsSource = Enumerable.Range(1, atomPages.Length).Select(i => $"{i} / {atomPages.Length}").ToArray(), SelectedIndex = 0 };
            paging.SelectedIndexChanged += (_, _) => { if (paging.SelectedIndex >= 0) preview.Content = new RubyTextView(atomPages[paging.SelectedIndex], true, 1, null); };
            body.Add(paging);
        }
        body.Add(preview);
        body.Add(new Label { Text = T("Hint") });
        body.Add(_status);
        var stop = new Button { Text = T("Stop"), AutomationId = "Audio.Stop" };
        stop.Clicked += async (_, _) => { _playVersion++; _status.Text = T("Finalizing"); await audio.StopAsync(_owner); if (_visible && !_busy) _status.Text = T("Stopped"); };
        body.Add(stop);
        var play = new Button { Text = T("PlayDefault"), AutomationId = "Audio.PlayDefault" };
        play.Clicked += async (_, _) => await PlayAsync("target", targetId); _actions.Add(play); body.Add(play);
        body.Add(Action("Record", RecordAsync)); body.Add(Action("Import", ImportAsync));
        body.Add(Action("Settings", () => { AppInfo.ShowSettingsUI(); return Task.CompletedTask; }, false));
        body.Add(Action("Refresh", ReloadAsync, false));
        body.Add(new Label { Text = T("Drafts"), FontSize = 20, FontAttributes = FontAttributes.Bold });
        foreach (var draft in drafts)
        {
            var card = new VerticalStackLayout { Spacing = 6 };
            card.Add(new Label { Text = draft.Phase == "ready" ? T("Ready") + $" · {draft.Wave!.DurationMs / 1000.0:0.0}s" : T("Incomplete") });
            if (draft.Phase == "ready")
            {
                AddPreview(card, "draft", draft.Id);
                var label = new Entry { Placeholder = T("Name"), MaxLength = 120, Text = T(draft.SourceRole == "user" ? "Recording" : "Imported"), AutomationId = "Audio.Name" };
                card.Add(label);
                var makeDefault = new CheckBox { IsChecked = target.Personal, AutomationId = "Audio.DefaultCheck" };
                card.Add(new HorizontalStackLayout { Children = { makeDefault, new Label { Text = T("SetDefault"), VerticalTextAlignment = TextAlignment.Center } } });
                if (draft.Target != target)
                {
                    card.Add(new Label { Text = T("NeedsReview") });
                    card.Add(Action("Confirm", async () => { if (await ConfirmAsync()) await store.ReviewDraftAsync(draft, target); }));
                }
                else card.Add(Action("Save", async () => { var name = label.Text ?? ""; var preferred = makeDefault.IsChecked; await audio.StopAsync(_owner); await Task.Run(() => store.SaveAsync(draft, name, preferred)); }));
            }
            else card.Add(Action("Recover", async () => { await store.FinalizeAsync(draft.Id); }));
            card.Add(Action("Discard", async () =>
            {
                if (await DisplayAlertAsync(T("Discard"), T("DiscardHint"), T("Discard"), language["Library.Cancel"]))
                { await audio.StopAsync(_owner); await store.DiscardAsync(draft.Id); }
            }));
            body.Add(new Border { Padding = 10, Content = card });
        }
        body.Add(new Label { Text = T("Tracks"), FontSize = 20, FontAttributes = FontAttributes.Bold });
        if (tracks.Count == 0) body.Add(new Label { Text = T("NoTracks") });
        foreach (var track in tracks)
        {
            var card = new VerticalStackLayout { Spacing = 6 };
            card.Add(new Label { Text = track.Label + $" · {track.DurationMs / 1000.0:0.0}s", FontSize = 18 });
            card.Add(new Label { Text = T(track.SourceRole == "standard" ? "Standard" : track.SourceRole == "user" ? "Recording" : "Imported") + " · " + T(track.Eligible ? track.Preferred ? "Default" : "Confirmed" : "NeedsReview") });
            AddPreview(card, "track", track.Id);
            if (track.SourceRole == "user") card.Add(Action("ShareRecording", async () =>
            {
                if (!await DisplayAlertAsync(T("ShareRecording"), T("ShareRecordingHint"), T("ShareRecording"), language["Library.Cancel"])) return;
                await audio.StopAsync(_owner);
                var files = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Packages.ShareFileStore>();
                var path = await Task.Run(() => files.CreateAsync(".wav", stream => store.ExportRecordingAsync(target, track.Id, stream)));
                await Share.Default.RequestAsync(new ShareFileRequest { Title = T("ShareRecording"), File = new ShareFile(path, "audio/wav") });
                _status.Text = language["Transfer.ShareOpened"];
            }, false));
            if (track.SourceRole == "user") card.Add(Action("SaveFile", async () =>
            {
                await audio.StopAsync(_owner);
                var files = Handler!.MauiContext!.Services.GetRequiredService<HanMate.Infrastructure.Packages.ShareFileStore>();
                var path = await Task.Run(() => files.CreateAsync(".wav", stream => store.ExportRecordingAsync(target, track.Id, stream)));
                _status.Text = language[await Files.NativeFileSaver.SaveAsync(path) ? "Transfer.FileSaved" : "Transfer.Cancelled"];
            }, false));
            if (track.Eligible) card.Add(Action("SetDefault", () => store.UpdateTrackAsync(target, track.Id, "default")));
            else if (track.SourceRole != "standard") card.Add(Action("Confirm", async () => { if (await ConfirmAsync()) await store.UpdateTrackAsync(target, track.Id, "confirm"); }));
            if (track.SourceRole != "standard") card.Add(Action("Remove", async () =>
            {
                if (await DisplayAlertAsync(T("Remove"), T("RemoveHint"), T("Remove"), language["Library.Cancel"]))
                { await audio.StopAsync(_owner); await store.UpdateTrackAsync(target, track.Id, "remove"); }
            }));
            body.Add(new Border { Padding = 10, Content = card });
        }
        Content = new ScrollView { Content = body };
    }
    private Task<bool> ConfirmAsync() => DisplayAlertAsync(T("Confirm"), T("ConfirmHint"), T("Confirm"), language["Library.Cancel"]);

    private void AddPreview(VerticalStackLayout body, string kind, Guid id)
    {
        var button = new Button { Text = T("Preview"), AutomationId = "Audio.Preview" };
        button.Clicked += async (_, _) => await PlayAsync(kind, id); _actions.Add(button); body.Add(button);
    }
    private async Task PlayAsync(string kind, Guid id)
    {
        if (_busy) return;
        var version = ++_playVersion; _status.Text = T("Playing");
        var outcome = await audio.PlayAsync(_owner, $"local:{kind}:{id:D}");
        if (_visible && version == _playVersion) _status.Text = T(outcome switch { PlaybackOutcome.Completed => "Played", PlaybackOutcome.Cancelled => "Stopped", PlaybackOutcome.Busy => "Busy", _ => "NoPlayback" });
    }
    private async Task RecordAsync()
    {
        var target = _target ?? throw new InvalidOperationException();
        Exception? failure = null; var version = ++_playVersion;
        var outcome = await audio.RecordAsync(_owner, async token =>
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    var permission = await Permissions.CheckStatusAsync<Permissions.Microphone>();
                    if (permission != PermissionStatus.Granted && !_permissionDenied) permission = await Permissions.RequestAsync<Permissions.Microphone>();
                    token.ThrowIfCancellationRequested();
                    if (permission != PermissionStatus.Granted) { _permissionDenied = true; throw new UnauthorizedAccessException(); }
                });
                token.ThrowIfCancellationRequested();
                var draft = await store.BeginAsync(target, "user");
                await MainThread.InvokeOnMainThreadAsync(() => _status.Text = T("Recording"));
                await MainThread.InvokeOnMainThreadAsync(() => NativeWaveRecorder.CaptureAsync(store.CapturePath(draft.Id), elapsed =>
                    MainThread.BeginInvokeOnMainThread(() => { if (_visible && version == _playVersion && audio.IsRecording) _status.Text = T("Recording") + " · " + elapsed.ToString(@"mm\:ss"); }), token));
                // Stop/back/background cancellation ends capture, then persists a recoverable draft without that cancelled token.
                await store.FinalizeAsync(draft.Id);
            }
            catch (Exception error) { failure = error; throw; }
        });
        if (failure is not null && failure is not OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (outcome == PlaybackOutcome.Busy) _status.Text = T("Busy");
        else if (outcome == PlaybackOutcome.Failed) throw new IOException("Capture failed.");
    }
    private async Task ImportAsync()
    {
        var selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("Import"), FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["audio/wav", "audio/x-wav", "audio/wave", "audio/mpeg", "audio/mp4", "audio/x-m4a"],
            [DevicePlatform.WinUI] = [".wav", ".mp3", ".m4a"], [DevicePlatform.iOS] = ["com.microsoft.waveform-audio", "public.mp3", "com.apple.m4a-audio"],
            [DevicePlatform.MacCatalyst] = ["com.microsoft.waveform-audio", "public.mp3", "com.apple.m4a-audio"]
        }) });
        if (selected is null) return;
        await using var input = await selected.OpenReadAsync();
        Exception? failure = null; _status.Text = T("Decoding");
        var outcome = await audio.PlayOperationAsync(_owner, "audio-import:" + Guid.NewGuid(), async token =>
        {
            try { await Task.Run(() => AudioFileImport.ImportAsync(store, _target!, input, FileSystem.CacheDirectory, token), token); }
            catch (Exception e) { failure = e; throw; }
        });
        if (failure is not null && failure is not OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (outcome == PlaybackOutcome.Busy) _status.Text = T("Busy");
        else if (outcome == PlaybackOutcome.Cancelled) throw new OperationCanceledException();
        else if (outcome == PlaybackOutcome.Failed) throw new IOException("Audio import failed.");
    }
}
