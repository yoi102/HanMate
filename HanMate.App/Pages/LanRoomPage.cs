using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Infrastructure.Sharing;

namespace HanMate.App.Pages;

public sealed class LanRoomPage : ContentPage
{
    private readonly string _packagePath;
    private readonly LanAudioSettings _audio;
    private readonly LocalizationService _language;
    private readonly Picker _address = new();
    private readonly Entry _roomCode = new() { Keyboard = Keyboard.Numeric, MaxLength = 2,
        HorizontalTextAlignment = TextAlignment.Center, FontSize = 28, AutomationId = "LanRoom.Code" };
    private readonly Label _status = new();
    private readonly VerticalStackLayout _peers = new() { Spacing = 8 };
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, (CheckBox Check, Label Status)> _peerViews = [];
    private readonly Button _start = new() { AutomationId = "LanRoom.Start" };
    private readonly Button _send = new() { AutomationId = "LanRoom.Send" };
    private readonly Button _selectAll = new() { IsVisible = false };
    private LanShareRoom? _room;
    private string? _lastJoinFailure;
    private IDisposable? _discoveryAccess;
    private bool _starting, _sending;
    private string T(string key) => _language["LanShare." + key];

    public LanRoomPage(string packagePath, LanAudioSettings audio, LocalizationService language)
    {
        _packagePath = packagePath; _audio = audio; _language = language;
        Title = T("RoomTitle");
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        var refresh = new ToolbarItem { Text = "⟳", Order = ToolbarItemOrder.Primary,
            AutomationId = "LanRoom.Refresh" };
        SemanticProperties.SetDescription(refresh, T("RefreshDevices"));
        refresh.Clicked += (_, _) =>
        {
            if (_room is null) { _status.Text = T("StartFirst"); return; }
            foreach (var peer in _room.Peers) UpdateProgress(peer);
            _status.Text = _room.Peers.Count == 0 && _lastJoinFailure is not null
                ? T("JoinFailed") + " " + _lastJoinFailure
                : string.Format(T("DevicesFound"), _room.Peers.Count);
        };
        ToolbarItems.Add(refresh);
        _roomCode.Placeholder = "00";
        _address.Title = T("Address");
        _start.Text = T("StartRoom");
        _start.Clicked += async (_, _) => await StartAsync();
        _selectAll.Text = T("SelectAllDevices");
        LibraryLayout.Quiet(_selectAll);
        _selectAll.Clicked += (_, _) =>
        {
            if (_room is null) return;
            foreach (var peer in _room.Peers)
                if (_peerViews.TryGetValue(peer.Id, out var view) && view.Check.IsEnabled) view.Check.IsChecked = true;
        };
        _send.Text = T("Send"); _send.IsEnabled = false;
        _send.Clicked += async (_, _) => await SendAsync();
        var body = new VerticalStackLayout { Padding = 20, Spacing = 14, MaximumWidthRequest = 760 };
        body.Add(LibraryLayout.Muted(T("SameWifi")));
        LibraryLayout.Styled(_status, "LibraryMuted"); body.Add(_status);
        var room = new VerticalStackLayout { Spacing = 10 };
        room.Add(LibraryLayout.Muted(T("RoomCode")));
        var roomFields = new Grid { ColumnDefinitions = { new(new GridLength(96)), new(GridLength.Star) }, ColumnSpacing = 12 };
        roomFields.Add(_roomCode); roomFields.Add(_address, 1); room.Add(roomFields); room.Add(_start);
        body.Add(LibraryLayout.Surface(room, new Thickness(16)));
        var devices = new VerticalStackLayout { Spacing = 10 };
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        header.Add(new Label { Text = T("Devices"), FontSize = 18, FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center });
        header.Add(_selectAll, 1); devices.Add(header); devices.Add(_peers); devices.Add(_send);
        body.Add(LibraryLayout.Surface(devices, new Thickness(16)));
        Content = new ScrollView { Content = body };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var addresses = LanAddresses.Candidates();
            _address.ItemsSource = addresses.ToArray();
            _address.SelectedIndex = addresses.Count > 0 ? 0 : -1;
            if (addresses.Count == 0) _status.Text = T("NoWifi");
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("NoWifi"); }
    }

    private async Task StartAsync()
    {
        if (_starting || _room is not null) return;
        if (!LanRoomDiscovery.IsValidCode(_roomCode.Text)) { _status.Text = T("InvalidRoomCode"); return; }
        if (_address.SelectedItem is not string address) { _status.Text = T("NoWifi"); return; }
        _starting = true; _start.IsEnabled = false;
        try
        {
            _discoveryAccess = LanDiscoveryAccess.Hold();
            _room = await LanShareRoom.StartAsync(address, _roomCode.Text, DeviceInfo.Name);
            _lastJoinFailure = null;
            _room.ProgressChanged += UpdateProgress;
            _room.JoinFailed += error => MainThread.BeginInvokeOnMainThread(() =>
            {
                _lastJoinFailure = error;
                if (_room is not null) _status.Text = T("JoinFailed") + " " + error;
            });
            _roomCode.IsReadOnly = true;
            _address.IsEnabled = false;
            _status.Text = string.Format(T("RoomReadyCode"), _roomCode.Text);
        }
        catch (Exception error)
        {
            _discoveryAccess?.Dispose(); _discoveryAccess = null;
            System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); _start.IsEnabled = true;
        }
        finally { _starting = false; }
    }

    private void UpdateProgress(LanPeerProgress progress) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_room is null) return;
        if (!_peerViews.TryGetValue(progress.Id, out var row))
        {
            var check = new CheckBox { AutomationId = "LanRoom.Device." + progress.Id };
            check.CheckedChanged += (_, e) => { if (e.Value) _selected.Add(progress.Id); else _selected.Remove(progress.Id); UpdateSend(); };
            var status = new Label { VerticalTextAlignment = TextAlignment.Center };
            var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            grid.Add(check); grid.Add(status, 1); _peers.Add(grid);
            row = (check, status); _peerViews[progress.Id] = row;
        }
        var state = T("State." + progress.State);
        row.Status.Text = progress.Name + " · " + state + (progress.TotalBytes > 0
            ? $" · {Math.Min(100, progress.ReceivedBytes * 100 / progress.TotalBytes)}%" : "")
            + (string.IsNullOrEmpty(progress.Detail) ? "" : " · " + progress.Detail);
        if (progress.State is LanPeerState.Completed or LanPeerState.Rejected or LanPeerState.Failed)
        { row.Check.IsChecked = false; row.Check.IsEnabled = false; }
        _selectAll.IsVisible = _peerViews.Values.Count(view => view.Check.IsEnabled) > 1;
        UpdateSend();
    });

    private void UpdateSend() => _send.IsEnabled = !_sending && _room is not null && _selected.Count > 0;

    private async Task SendAsync()
    {
        if (_sending || _room is null || _selected.Count == 0) return;
        _sending = true; UpdateSend(); _status.Text = T("Sending");
        try
        {
            var selected = _selected.ToArray();
            await _room.SendAsync(selected, _packagePath, _audio);
            _selected.ExceptWith(selected); _status.Text = T("SendFinished");
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); }
        finally { _sending = false; UpdateSend(); }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        var room = _room; _room = null; UpdateSend();
        if (room is not null) { room.ProgressChanged -= UpdateProgress; await room.DisposeAsync(); }
        _discoveryAccess?.Dispose(); _discoveryAccess = null;
        _roomCode.IsReadOnly = false;
        _address.IsEnabled = true;
        _selected.Clear(); _peerViews.Clear(); _peers.Clear();
        _selectAll.IsVisible = false;
        _lastJoinFailure = null;
        _start.IsEnabled = true;
    }
}
