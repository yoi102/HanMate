using HanMate.App.Localization;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public sealed class ContentTransferPage : ContentPage
{
    private readonly ContentShareStore _share;
    private readonly ContentPackageImportStore _import;
    private readonly ShareFileStore _files;
    private readonly LocalizationService _language;
    private readonly HashSet<Guid> _selection;
    private readonly HashSet<Guid> _audioSelection = [];
    private readonly Label _audioSummary = new();
    private readonly Label _status = new() { AutomationId = "Transfer.Status" };
    private readonly Label _count = new();
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    private readonly List<Button> _buttons = [];
    private readonly CheckBox _attest = new() { AutomationId = "Transfer.Attest" };
    private int _offset;
    private bool _busy;
    private bool _openingAudio;
    private string? _export;
    private ContentSharePlan? _plan;
    private CancellationTokenSource? _operation;
    private Shell? _shell;
    private string T(string key) => _language["Transfer." + key];
    public ContentTransferPage(ContentShareStore share, ContentPackageImportStore import, ShareFileStore files, LocalizationService language, IEnumerable<Guid>? selected = null)
    {
        _share = share; _import = import; _files = files; _language = language; _selection = (selected ?? []).ToHashSet();
        Title = T("Title");
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = T("Hint") }); body.Add(_status); body.Add(_count);
        body.Add(Action("Import", ImportAsync)); body.Add(Action("Preview", PreviewAsync));
        body.Add(Action("ChooseAudio", ChooseAudioAsync)); body.Add(_audioSummary);
        var rights = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        rights.Add(_attest); rights.Add(new Label { Text = T("Attest"), VerticalTextAlignment = TextAlignment.Center }, 1); body.Add(rights);
        body.Add(Action("Export", ExportAsync)); body.Add(Action("Share", ShareAsync));
        body.Add(Action("SaveFile", token => SaveFileAsync(false, token)));
        body.Add(Action("SaveZip", token => SaveFileAsync(true, token)));
        body.Add(Action("Clear", token => { _selection.Clear(); Invalidate(); return LoadAsync(token); }));
        var cancel = new Button { Text = _language["Library.Cancel"], AutomationId = "Transfer.Cancel" }; cancel.Clicked += (_, _) => _operation?.Cancel(); body.Add(cancel);
        body.Add(_rows);
        body.Add(Action("Previous", token => { _offset = Math.Max(0, _offset - 50); return LoadAsync(token); }));
        body.Add(Action("Next", token => { _offset += 50; return LoadAsync(token); }));
        Content = new ScrollView { Content = body }; UpdateCount();
    }
    private Button Action(string key, Func<CancellationToken, Task> action)
    {
        var button = new Button { Text = T(key), AutomationId = "Transfer." + key };
        button.Clicked += async (_, _) => await RunAsync(action); _buttons.Add(button); return button;
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return; _busy = true; _rows.IsEnabled = _attest.IsEnabled = false; foreach (var b in _buttons) b.IsEnabled = false;
        using var operation = new CancellationTokenSource(); _operation = operation;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { _status.Text = T("Cancelled"); }
        catch (Files.FileSaveException) { _status.Text = T("SaveFailed"); }
        catch (PackageException e) { _status.Text = T(e.Code switch { "PLAN_STALE" => "Stale", "SHARE_RIGHTS_REQUIRED" => "Rights", "SHARE_CACHE_FULL" => "CacheFull", "PACKAGE_ID_COLLISION" => "Collision", _ => "Invalid" }); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.GetType().Name); _status.Text = T("Failed"); }
        finally { _operation = null; _busy = false; _rows.IsEnabled = _attest.IsEnabled = true; foreach (var b in _buttons) b.IsEnabled = true; }
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RunAsync(LoadAsync); }
    private void UpdateCount() => _count.Text = string.Format(T("Selected"), _selection.Count);
    private void Invalidate() { _plan = null; _export = null; _attest.IsChecked = false; _audioSelection.Clear(); _audioSummary.Text = ""; _status.Text = T("Changed"); UpdateCount(); }
    private async Task LoadAsync(CancellationToken token)
    {
        var rows = await Task.Run(() => _share.ListAsync(_offset, token), token);
        if (rows.Count == 0 && _offset > 0) { _offset = Math.Max(0, _offset - 50); rows = await Task.Run(() => _share.ListAsync(_offset, token), token); }
        _rows.Clear();
        foreach (var row in rows)
        {
            var check = new CheckBox { IsChecked = _selection.Contains(row.Id), AutomationId = "Transfer.Select." + row.Id };
            SemanticProperties.SetDescription(check, row.Title);
            check.CheckedChanged += (_, e) => { if (e.Value) _selection.Add(row.Id); else _selection.Remove(row.Id); Invalidate(); };
            var kind = row.Kind == HanMate.Core.Content.ContentKind.Grammar ? _language["Learn.Grammar"] : _language["Kind." + row.Kind];
            var text = new Label { Text = row.Title + " · " + kind + "\n" + row.License + " · " + T(row.CanShare ? "Allowed" : row.CanAttest ? "NeedsRights" : "Restricted"), VerticalTextAlignment = TextAlignment.Center };
            var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; grid.Add(check); grid.Add(text, 1); _rows.Add(grid);
        }
    }
    private async Task PreviewAsync(CancellationToken token)
    {
        _plan = null; _export = null; _attest.IsChecked = false; _audioSelection.Clear(); _audioSummary.Text = "";
        var selected = _selection.ToArray(); _plan = await Task.Run(() => _share.PreviewAsync(selected, token), token);
        _status.Text = string.Format(T("PreviewInfo"), _plan.Items.Count, _plan.Audio, _plan.Drafts, _plan.Blocked, _plan.NeedAttestation)
            + "\n" + string.Join("\n", _plan.Items.Select(i => i.Title));
        UpdateAudioSummary();
    }
    private async Task ChooseAudioAsync(CancellationToken token)
    {
        if (_plan is null) { _status.Text = T("PreviewFirst"); return; }
        var plan = _plan;
        _openingAudio = true;
        try { await Navigation.PushAsync(new ContentAudioSelectionPage(plan.AudioTracks, _audioSelection, _language, selected =>
            { if (_plan != plan) return; _audioSelection.Clear(); _audioSelection.UnionWith(selected); _export = null; UpdateAudioSummary(); })); }
        finally { _openingAudio = false; }
    }
    private void UpdateAudioSummary()
    {
        if (_plan is null) return;
        var tracks = _plan.AudioTracks.Where(t => _audioSelection.Contains(t.Id)).ToArray();
        _audioSummary.Text = string.Format(T("AudioSelected"), tracks.Length, _plan.Audio - tracks.Length, tracks.Sum(t => t.ByteLength) / 1024.0);
    }
    private async Task ExportAsync(CancellationToken token)
    {
        if (_plan is null) { _status.Text = T("PreviewFirst"); return; }
        var plan = _plan; var attest = _attest.IsChecked;
        _export = null;
        var selectedAudio = _audioSelection.ToArray();
        if (selectedAudio.Length > 0 && !await DisplayAlertAsync(T("ChooseAudio"), T("AudioConsent"), T("Export"), _language["Library.Cancel"])) { _status.Text = T("Cancelled"); return; }
        _export = await Task.Run(() => _files.CreateAsync(".hanpack", stream => _share.ExportAsync(plan, stream, attest, selectedAudio, selectedAudio.Length > 0, token), token), token);
        _status.Text = T("Exported");
    }
    private async Task ShareAsync(CancellationToken token)
    {
        if (_export is null) { _status.Text = T("ExportFirst"); return; }
        token.ThrowIfCancellationRequested();
        await Share.Default.RequestAsync(new ShareFileRequest { Title = T("Share"), File = new ShareFile(_export, "application/zip") });
        _status.Text = T("ShareOpened");
    }
    private async Task SaveFileAsync(bool zip, CancellationToken token)
    {
        if (_export is null) { _status.Text = T("ExportFirst"); return; }
        token.ThrowIfCancellationRequested();
        _status.Text = T(await Files.NativeFileSaver.SaveAsync(_export, zip) ? "FileSaved" : "Cancelled");
    }
    private async Task ImportAsync(CancellationToken token)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("Import") }); if (file is null) { _status.Text = T("Cancelled"); return; }
        await using var stream = await file.OpenReadAsync();
        var plan = await Task.Run(() => _import.PlanAsync(stream, token), token);
        var message = string.Format(T("ImportPreview"), plan.Added, plan.Reused, plan.Conflicts)
            + "\n" + string.Format(T("AudioImport"), plan.AudioAdded, plan.AudioReused, plan.AudioNeedsReview, plan.AudioBytes / 1024.0)
            + "\n" + string.Join("\n", plan.Titles.Take(10));
        if (!await DisplayAlertAsync(T("Import"), message, T("Import"), _language["Library.Cancel"])) { _status.Text = T("Cancelled"); return; }
        var result = await Task.Run(() => _import.CommitAsync(plan, token), token);
        Invalidate(); await LoadAsync(CancellationToken.None); _status.Text = string.Format(T("Imported"), result.Added, result.Reused);
    }
    protected override bool OnBackButtonPressed() => _busy || base.OnBackButtonPressed();
    protected override void OnHandlerChanged()
    { base.OnHandlerChanged(); if (_shell is not null) _shell.Navigating -= Leaving; _shell = Handler is null ? null : Shell.Current; if (_shell is not null) _shell.Navigating += Leaving; }
    private void Leaving(object? sender, ShellNavigatingEventArgs e) { if (_shell?.CurrentPage == this && _busy && !_openingAudio && e.CanCancel) e.Cancel(); }
    public static ContentTransferPage Create(IServiceProvider services, LocalizationService language, params Guid[] selected) => new(
        services.GetRequiredService<ContentShareStore>(), services.GetRequiredService<ContentPackageImportStore>(), services.GetRequiredService<ShareFileStore>(), language, selected);
}
