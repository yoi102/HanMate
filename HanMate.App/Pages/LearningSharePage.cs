using System.Text.Json;
using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Sharing;

namespace HanMate.App.Pages;

/// <summary>Selects complete learning entries and prepares the existing validated content package for a LAN room.</summary>
public sealed class LearningSharePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly LocalizationService _language;
    private readonly HashSet<Guid> _selected;
    private readonly HashSet<Guid> _selectedAudio = [];
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    private readonly Label _status = new(), _selectedLabel = new(), _audioLabel = new();
    private readonly CheckBox _rights = new(), _includeAudio = new() { IsChecked = true };
    private readonly Picker _kind = new();
    private readonly Button _previous = new(), _next = new(), _send = new();
    private ContentSharePlan? _plan;
    private int _offset;
    private bool _busy, _openingAudio;
    private readonly ContentKind[] _kinds = [ContentKind.Word, ContentKind.Text, ContentKind.Grammar, ContentKind.Poem];
    private string T(string key) => _language["LanShare." + key];

    public LearningSharePage(IServiceProvider services, LocalizationService language, IEnumerable<Guid>? selected = null,
        ContentKind initialKind = ContentKind.Word)
    {
        _services = services; _language = language; _selected = (selected ?? []).ToHashSet();
        Title = T("SelectTitle");
        _kind.ItemsSource = _kinds.Select(k => k == ContentKind.Grammar ? language.KindGrammar : language["Kind." + k]).ToArray();
        _kind.SelectedIndex = Math.Max(0, Array.IndexOf(_kinds, initialKind));
        _kind.SelectedIndexChanged += async (_, _) => { if (_busy) return; _offset = 0; await LoadAsync(); };
        _previous.Text = language["Library.Previous"]; _next.Text = language["Library.Next"];
        _previous.Clicked += async (_, _) => { if (_busy) return; _offset = Math.Max(0, _offset - 50); await LoadAsync(); };
        _next.Clicked += async (_, _) => { if (_busy) return; _offset += 50; await LoadAsync(); };
        var clear = new Button { Text = T("Clear") };
        clear.Clicked += async (_, _) => { if (_busy) return; _selected.Clear(); Changed(); await LoadAsync(); };
        var selectPage = new Button { Text = T("SelectPage") };
        selectPage.Clicked += async (_, _) =>
        {
            if (_busy) return;
            var rows = await CurrentRowsAsync();
            foreach (var row in rows.Items.Where(row =>
            {
                var content = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
                var share = ContentShareStore.Row(content);
                return share.CanShare || share.CanAttest;
            }).Take(Math.Max(0, 100 - _selected.Count))) _selected.Add(row.Id);
            Changed(); await LoadAsync();
        };
        var audio = new Button { Text = T("ChooseAudio") };
        audio.Clicked += async (_, _) => await ChooseAudioAsync();
        _send.Text = T("CreateRoom"); _send.AutomationId = "LearningShare.CreateRoom";
        _send.Clicked += async (_, _) => await CreateRoomAsync();
        var body = new VerticalStackLayout { Padding = 18, Spacing = 10, MaximumWidthRequest = 800 };
        body.Add(new Label { Text = T("Hint") }); body.Add(_status); body.Add(_selectedLabel);
        body.Add(_kind);
        body.Add(new HorizontalStackLayout { Spacing = 8, Children = { selectPage, clear } });
        body.Add(_rows);
        body.Add(new HorizontalStackLayout { Spacing = 8, Children = { _previous, _next } });
        body.Add(new HorizontalStackLayout { Spacing = 8, Children = { _includeAudio,
            new Label { Text = T("IncludeAudio"), VerticalTextAlignment = TextAlignment.Center } } });
        body.Add(audio); body.Add(_audioLabel);
        body.Add(new HorizontalStackLayout { Spacing = 8, Children = { _rights,
            new Label { Text = T("ConfirmRights"), VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap } } });
        body.Add(_send);
        Content = new ScrollView { Content = body };
        Changed();
    }

    protected override async void OnAppearing() { base.OnAppearing(); if (!_openingAudio) await LoadAsync(); }

    private async Task<LearningResult> CurrentRowsAsync() => await _services.GetRequiredService<LearningCatalogStore>()
        .QueryAsync(new(_kinds[Math.Max(0, _kind.SelectedIndex)]), _offset);

    private async Task LoadAsync()
    {
        if (_busy || Handler is null) return;
        try
        {
            var result = await CurrentRowsAsync();
            if (_offset > 0 && result.Items.Count == 0) { _offset = Math.Max(0, _offset - 50); result = await CurrentRowsAsync(); }
            _rows.Clear();
            foreach (var row in result.Items)
            {
                var content = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
                var check = new CheckBox { IsChecked = _selected.Contains(row.Id), AutomationId = "LearningShare.Select." + row.Id };
                var label = new Label { Text = row.Title, VerticalTextAlignment = TextAlignment.Center };
                var shareable = ContentShareStore.Row(content);
                if (!shareable.CanShare && !shareable.CanAttest)
                { check.IsEnabled = false; label.Text += " · " + T("Restricted"); }
                check.CheckedChanged += (_, e) =>
                {
                    if (e.Value && _selected.Count >= 100) { check.IsChecked = false; _status.Text = T("Limit"); return; }
                    if (e.Value) _selected.Add(row.Id); else _selected.Remove(row.Id);
                    Changed();
                };
                var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
                grid.Add(check); grid.Add(label, 1); _rows.Add(grid);
            }
            _previous.IsEnabled = _offset > 0; _next.IsEnabled = _offset + 50 < result.Total;
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); }
    }

    private void Changed()
    {
        _plan = null; _selectedAudio.Clear(); _audioLabel.Text = "";
        _selectedLabel.Text = string.Format(T("Selected"), _selected.Count);
    }

    private async Task<ContentSharePlan?> PrepareAsync()
    {
        if (_selected.Count is < 1 or > 100) { _status.Text = T("SelectFirst"); return null; }
        var plan = await _services.GetRequiredService<ContentShareStore>().PreviewAsync(_selected.ToArray());
        if (plan.Blocked > 0)
        {
            _status.Text = string.Format(T("Blocked"), plan.Blocked); return null;
        }
        if (plan.NeedAttestation > 0 && !_rights.IsChecked)
        {
            _status.Text = T("RightsNeeded"); return null;
        }
        if (_plan is null) _selectedAudio.UnionWith(plan.AudioTracks.Where(t => t.CanShare).Select(t => t.Id));
        _plan = plan;
        _audioLabel.Text = string.Format(T("AudioSummary"), _selectedAudio.Count, plan.Audio);
        return plan;
    }

    private async Task ChooseAudioAsync()
    {
        if (_busy) return;
        try
        {
            var plan = await PrepareAsync(); if (plan is null) return;
            _openingAudio = true;
            await Navigation.PushAsync(new ContentAudioSelectionPage(plan.AudioTracks, _selectedAudio, _language, selected =>
            {
                _selectedAudio.Clear(); _selectedAudio.UnionWith(selected);
                _audioLabel.Text = string.Format(T("AudioSummary"), _selectedAudio.Count, plan.Audio);
            }));
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); }
        finally { _openingAudio = false; }
    }

    private async Task CreateRoomAsync()
    {
        if (_busy) return; _busy = true; _send.IsEnabled = false;
        _kind.IsEnabled = false; _rows.IsEnabled = false; _status.Text = T("Preparing");
        try
        {
            var plan = await PrepareAsync(); if (plan is null) return;
            var audio = _includeAudio.IsChecked ? _selectedAudio.ToArray() : [];
            if (audio.Length > 0 && !await DisplayAlertAsync(T("ChooseAudio"), _language["Transfer.AudioConsent"],
                T("CreateRoom"), _language["Library.Cancel"])) { _status.Text = T("Cancelled"); return; }
            var file = await _services.GetRequiredService<ShareFileStore>().CreateAsync(".hanpack",
                stream => _services.GetRequiredService<ContentShareStore>().ExportAsync(plan, stream,
                    _rights.IsChecked, audio, audio.Length > 0));
            if (new FileInfo(file).Length > LanShareRoom.MaxPackageBytes)
            {
                File.Delete(file); _status.Text = T("PackageTooLarge"); return;
            }
            var engine = SpeechPreferences.Engine;
            var speaker = engine == SpeechEngine.System ? null :
                await _services.GetRequiredService<SpeechVoicePacks>().Get(engine).ReadSelectionAsync();
            _status.Text = T("Ready");
            await Navigation.PushAsync(new LanRoomPage(file, new(engine.ToString(), audio.Length, speaker), _language));
        }
        catch (PackageException error)
        { _status.Text = error.Code == "PLAN_STALE" ? T("Changed") : error.Code == "SHARE_RIGHTS_REQUIRED" ? T("RightsNeeded") : T("Failed"); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); }
        finally { _busy = false; _send.IsEnabled = true; _kind.IsEnabled = true; _rows.IsEnabled = true; }
    }
}
