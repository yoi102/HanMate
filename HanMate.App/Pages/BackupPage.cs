using HanMate.Core.Audio;
using HanMate.App.Localization;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public sealed class BackupPage : ContentPage
{
    private readonly BackupExportStore _exporter;
    private readonly BackupReplacementStore _replacement;
    private readonly SafetyRecoveryStore _recovery;
    private readonly OrphanAudioStore _orphans;
    private readonly VerticalStackLayout _recoveryRows = new() { Spacing = 8 };
    private readonly PlaybackCoordinator _playback;
    private readonly BackupImportStore _importer;
    private readonly ShareFileStore _files;
    private readonly LocalizationService _language;
    private readonly Label _status = new() { AutomationId = "Backup.Status" };
    private readonly List<Button> _buttons = [];
    private readonly CheckBox _partial = new() { AutomationId = "Backup.AcceptPartial" };
    private readonly CheckBox _settings = new() { AutomationId = "Backup.ApplySettings" };
    private View? _partialRow;
    private BackupExportPlan? _preview;
    private string? _output;
    private CancellationTokenSource? _operation;
    private Shell? _shell;
    private bool _busy;
    private string T(string key) => _language["Backup." + key];
    public BackupPage(BackupExportStore exporter, BackupImportStore importer, ShareFileStore files, LocalizationService language, BackupReplacementStore replacement, PlaybackCoordinator playback, SafetyRecoveryStore recovery, OrphanAudioStore orphans)
    { _recovery = recovery; _orphans = orphans; _replacement = replacement; _playback = playback; _exporter = exporter; _importer = importer; _files = files; _language = language; Render(); }
    private void Render()
    {
        // A native view may have only one parent. Detach retained controls before rebuilding after a language restore.
        if (_status.Parent is Layout statusParent) statusParent.Remove(_status);
        if (_partial.Parent is Layout partialParent) partialParent.Remove(_partial);
        if (_settings.Parent is Layout settingsParent) settingsParent.Remove(_settings);
        if (_recoveryRows.Parent is Layout recoveryParent) recoveryParent.Remove(_recoveryRows);
        _recoveryRows.Clear();
        Title = T("Title"); _buttons.Clear();
        var body = new VerticalStackLayout { Padding = 16, Spacing = 12 };
        body.Add(new Label { Text = T("Hint") }); body.Add(_status);
        body.Add(Action("Preview", PreviewAsync)); _partialRow = Check(_partial, T("Partial"));
        _partialRow.IsVisible = _preview?.MissingResources.Count > 0; body.Add(_partialRow);
        body.Add(Action("Export", ExportAsync)); body.Add(Action("Share", ShareAsync));
        body.Add(Action("SaveFile", token => SaveFileAsync(false, token)));
        body.Add(Action("SaveZip", token => SaveFileAsync(true, token)));
        body.Add(Check(_settings, T("ApplySettings"))); body.Add(Action("Import", ImportAsync));
        body.Add(Action("Replace", ReplaceAsync)); body.Add(Action("Recovery", RecoveryAsync));
        body.Add(_recoveryRows); body.Add(Action("RetainRecent", RetainRecentAsync)); body.Add(Action("ScanAudio", ScanAudioAsync));
        var cancel = new Button { Text = _language["Library.Cancel"], AutomationId = "Backup.Cancel" }; cancel.Clicked += (_, _) => _operation?.Cancel(); body.Add(cancel);
        Content = new ScrollView { Content = body };
    }
    private static View Check(CheckBox check, string text)
    { var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; grid.Add(check); grid.Add(new Label { Text = text, VerticalTextAlignment = TextAlignment.Center }, 1); return grid; }
    private Button Action(string key, Func<CancellationToken, Task> action)
    {
        var button = new Button { Text = T(key), AutomationId = "Backup." + key };
        button.Clicked += async (_, _) => await RunAsync(action); _buttons.Add(button); return button;
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return; _busy = true;
        using var cancel = new CancellationTokenSource(); _operation = cancel; Enable(false);
        try { await action(cancel.Token); }
        catch (OperationCanceledException) { _status.Text = _language["Transfer.Cancelled"]; }
        catch (Files.FileSaveException) { _status.Text = _language["Transfer.SaveFailed"]; }
        catch (PackageException e)
        { _status.Text = e.Code switch { "PLAN_STALE" => _language["Transfer.Stale"], "BACKUP_PARTIAL_CONFIRMATION_REQUIRED" => T("Partial"),
            "BACKUP_DRAFTS_EXIST" => T("DraftsBlock"), "BACKUP_REFERENCES_EXIST" => T("ReferencesBlock"), "BACKUP_SAFETY_REQUIRED" => T("SafetyRequired"),
            "BACKUP_RIGHTS_REQUIRED" => T("Rights"), "BACKUP_INCOMPATIBLE" => T("SnapshotIncompatible"), "BACKUP_RECORDING_BUSY" => T("RecordingBusy"), "PACKAGE_LIMIT_EXCEEDED" => T("Limit"),
            "PACKAGE_ID_COLLISION" => _language["Transfer.Collision"], _ => T("Invalid") }; }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.GetType().Name); _status.Text = _language["Transfer.Failed"]; }
        finally { _operation = null; _busy = false; Enable(true); }
    }
    private void Enable(bool enabled) { foreach (var b in _buttons) b.IsEnabled = enabled; _partial.IsEnabled = _settings.IsEnabled = enabled; }
    private async Task PreviewAsync(CancellationToken token)
    {
        _preview = null; _output = null; _partial.IsChecked = false; _status.Text = T("Working");
        if (_partialRow is not null) _partialRow.IsVisible = false;
        _preview = await Task.Run(() => _exporter.PreviewAsync(token), token);
        if (_partialRow is not null) _partialRow.IsVisible = _preview.MissingResources.Count > 0;
        _status.Text = string.Format(T("PreviewInfo"), _preview.Contents, _preview.Folders, _preview.Favorites, _preview.Audio,
            _preview.AudioBytes / 1024.0, _preview.IncludedResources, _preview.MissingResources.Count, _preview.Drafts, _preview.Trash)
            + "\n" + string.Join("\n", _preview.MissingResources.Take(100));
    }
    private async Task ExportAsync(CancellationToken token)
    {
        if (_preview is null) { _status.Text = _language["Transfer.PreviewFirst"]; return; }
        var plan = _preview; var accept = _partial.IsChecked; _output = null; _status.Text = T("Working");
        _output = await Task.Run(() => _files.CreateAsync(".hanbackup", stream => _exporter.ExportAsync(plan, stream, accept, token), token), token);
        _status.Text = T("Exported");
    }
    private async Task ShareAsync(CancellationToken token)
    {
        if (_output is null) { _status.Text = T("ExportFirst"); return; }
        token.ThrowIfCancellationRequested();
        await Share.Default.RequestAsync(new ShareFileRequest { Title = T("Title"), File = new ShareFile(_output, "application/zip") });
        _status.Text = _language["Transfer.ShareOpened"];
    }
    private async Task SaveFileAsync(bool zip, CancellationToken token)
    {
        if (_output is null) { _status.Text = T("ExportFirst"); return; }
        token.ThrowIfCancellationRequested();
        _status.Text = _language[await Files.NativeFileSaver.SaveAsync(_output, zip) ? "Transfer.FileSaved" : "Transfer.Cancelled"];
    }
    private async Task ImportAsync(CancellationToken token)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("Import") });
        if (file is null) { _status.Text = _language["Transfer.Cancelled"]; return; }
        token.ThrowIfCancellationRequested(); _status.Text = T("Working"); await using var stream = await file.OpenReadAsync(); var settings = _settings.IsChecked;
        var plan = await Task.Run(() => _importer.PlanAsync(stream, settings, token), token);
        var message = string.Format(T("ImportInfo"), plan.Added, plan.Reused, plan.Conflicts, plan.AudioAdded, plan.FoldersAdded,
            plan.Favorites, plan.ResourcesInstalled, plan.ResourcesKept, plan.MissingResources.Count, plan.FolderConflicts)
            + "\n" + T(plan.ApplySettings ? "SettingsWillApply" : "SettingsKept") + "\n" + string.Join("\n", plan.MissingResources);
        if (!await DisplayAlertAsync(T("Import"), message, T("Import"), _language["Library.Cancel"])) { _status.Text = _language["Transfer.Cancelled"]; return; }
        token.ThrowIfCancellationRequested(); var result = await Task.Run(() => _importer.CommitAsync(plan, token), token);
        _preview = null; _output = null;
        if (_partialRow is not null) _partialRow.IsVisible = false;
        if (plan.ApplySettings) { await _language.ReloadSavedAsync(); Render(); }
        _status.Text = string.Format(T("Imported"), result.Added, result.Reused, plan.MissingResources.Count);
    }
    private async Task ReplaceAsync(CancellationToken token)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("Replace") });
        if (file is null) { _status.Text = _language["Transfer.Cancelled"]; return; }
        await _playback.StopAsync();
        await using var input = await file.OpenReadAsync();
        var plan = await Task.Run(() => _replacement.PlanAsync(input, token), token);
        var message = string.Format(T("ReplaceInfo"), plan.DeletedContents, plan.DeletedAudio, plan.DeletedFolders, plan.DeletedFavorites,
            plan.Import.Added, plan.Import.MissingResources.Count) + "\n" + string.Join("\n", plan.Import.MissingResources);
        if (!await DisplayAlertAsync(T("Replace"), message, T("PrepareSafety"), _language["Library.Cancel"])) { _status.Text = _language["Transfer.Cancelled"]; return; }
        _status.Text = T("Working");
        await Task.Run(() => _replacement.PrepareSafetyAsync(plan, token), token);
        if (!await DisplayAlertAsync(T("Replace"), T("ReplaceConfirm"), T("Replace"), _language["Library.Cancel"])) { _status.Text = _language["Transfer.Cancelled"]; return; }
        var result = await Task.Run(() => _replacement.CommitAsync(plan, token), token);
        _preview = null; _output = null; await _language.ReloadSavedAsync(); Render();
        _status.Text = string.Format(T("Imported"), result.Added, result.Reused, plan.Import.MissingResources.Count);
    }
    private async Task RecoveryAsync(CancellationToken token)
    {
        _status.Text = T("Working");
        foreach (var old in _recoveryRows.Children.OfType<Button>()) _buttons.Remove(old);
        _recoveryRows.Clear();
        var records = await Task.Run(() => _recovery.ListAsync(token), token);
        _status.Text = records.Count == 0 ? T("NoRecovery") : string.Format(T("SnapshotTotal"), records.Count, records.Sum(r => r.Bytes) / 1048576.0);
        foreach (var r in records)
        {
            _recoveryRows.Add(new Label { Text = $"{r.CreatedUtc.ToLocalTime():g} · {r.Id.ToString()[..8]}\n" +
                (r.Status == "valid" ? string.Format(T("SnapshotValid"), r.Bytes / 1048576.0) : T(r.Status == "incompatible" ? "SnapshotIncompatible" : "SnapshotDamaged")) +
                "\n" + T(r.Committed ? "RecoveryCommitted" : "RecoveryUncommitted") });
            if (r.Status == "valid")
            {
                var button = Action("RestoreSnapshot", ct => RestoreSnapshotAsync(r.Id, ct));
                button.AutomationId = "Backup.Restore." + r.Id.ToString("N"); _recoveryRows.Add(button);
            }
        }
    }
    private async Task RestoreSnapshotAsync(Guid id, CancellationToken token)
    {
        await _playback.StopAsync();
        var plan = await Task.Run(() => _recovery.PlanAsync(id, token), token);
        if (!await DisplayAlertAsync(T("RestoreSnapshot"), string.Format(T("SnapshotImpact"), plan.CurrentContents, plan.Contents, plan.Audio, plan.Folders, plan.Trash),
            T("PrepareSafety"), _language["Library.Cancel"])) return;
        _status.Text = T("Working"); await Task.Run(() => _recovery.PrepareAsync(plan, token), token);
        if (!await DisplayAlertAsync(T("RestoreSnapshot"), T("SnapshotConfirm"), T("RestoreSnapshot"), _language["Library.Cancel"]))
        { _status.Text = _language["Transfer.Cancelled"]; return; }
        await Task.Run(() => _recovery.RestoreAsync(plan, token), token);
        _preview = null; _output = null; await _language.ReloadSavedAsync(); Render();
        _status.Text = string.Format(T("SnapshotRestored"), plan.OperationId.ToString()[..8]);
    }
    private async Task RetainRecentAsync(CancellationToken token)
    {
        var plan = await Task.Run(() => _recovery.PlanRetentionAsync(token), token);
        if (!await DisplayAlertAsync(T("RetainRecent"), string.Format(T("RetentionInfo"), plan.Items.Count, plan.Bytes / 1048576.0), T("Clean"), _language["Library.Cancel"])) return;
        await Task.Run(() => _recovery.RetainRecentAsync(plan, token), token);
        await RecoveryAsync(token);
    }
    private async Task ScanAudioAsync(CancellationToken token)
    {
        _status.Text = T("Working");
        var plan = await Task.Run(() => _orphans.ScanAsync(token), token);
        if (plan.BlockedReason is not null)
        {
            _status.Text = T("CleanupProtected") + "\n" + T(plan.BlockedReason == "BACKUP_DRAFTS_EXIST" ? "CleanupDrafts" : plan.BlockedReason == "BACKUP_RECORDING_BUSY" ? "RecordingBusy" : "SafetyRequired"); return;
        }
        var count = plan.Items.Count(x => x.Eligible);
        _status.Text = string.Format(T("CleanupInfo"), plan.Items.Count, count, plan.ReclaimableBytes / 1048576.0, plan.Items.Count - count);
        if (count == 0 || !await DisplayAlertAsync(T("ScanAudio"), _status.Text + "\n" + T("CleanupConfirm"), T("Clean"), _language["Library.Cancel"])) return;
        var result = await Task.Run(() => _orphans.CleanAsync(plan, token), token);
        _status.Text = string.Format(T("CleanupResult"), result.Deleted, result.Bytes / 1048576.0, result.Skipped);
    }
    protected override bool OnBackButtonPressed() => _busy || base.OnBackButtonPressed();
    protected override void OnDisappearing() { _operation?.Cancel(); base.OnDisappearing(); }
    protected override void OnHandlerChanged()
    { base.OnHandlerChanged(); if (_shell is not null) _shell.Navigating -= Leaving; _shell = Handler is null ? null : Shell.Current; if (_shell is not null) _shell.Navigating += Leaving; }
    private void Leaving(object? sender, ShellNavigatingEventArgs e) { if (_shell?.CurrentPage == this && _busy && e.CanCancel) e.Cancel(); }
}
