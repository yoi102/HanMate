using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Sharing;

namespace HanMate.App.Pages;

public sealed class LanReceivePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly LocalizationService _language;
    private readonly Entry _code = new() { Keyboard = Keyboard.Numeric, MaxLength = 2,
        HorizontalTextAlignment = TextAlignment.Center, FontSize = 28, AutomationId = "LanReceive.Code" };
    private readonly Picker _rooms = new() { AutomationId = "LanReceive.Rooms" };
    private IReadOnlyList<LanDiscoveredRoom> _found = [];
    private readonly Label _status = new();
    private readonly VerticalStackLayout _body = new() { Padding = 18, Spacing = 12, MaximumWidthRequest = 760 };
    private readonly VerticalStackLayout _matches = new() { Spacing = 10 };
    private readonly Button _cancel = new() { IsVisible = false };
    private readonly Entry _senderAddress = new() { IsVisible = false, AutomationId = "LanReceive.SenderAddress" };
    private Border? _matchesSurface;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _searchCancellation;
    private bool _busy, _searching;
    private string T(string key) => _language["LanShare." + key];

    public LanReceivePage(IServiceProvider services, LocalizationService language)
    {
        _services = services; _language = language; Title = T("ReceiveTitle");
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        _body.Padding = 20; _body.Spacing = 14;
        _code.Placeholder = "00";
        _code.TextChanged += (_, _) =>
        {
            if (_searching || _busy) return;
            _found = []; _rooms.ItemsSource = null;
            if (_matchesSurface is not null) _matchesSurface.IsVisible = false;
        };
        var refresh = new Button { Text = T("FindRoom"), AutomationId = "LanReceive.Refresh" };
        refresh.Clicked += async (_, _) => await SearchAsync();
        var join = new Button { Text = T("Join"), AutomationId = "LanReceive.Join" };
        join.Clicked += async (_, _) =>
        {
            if (_rooms.SelectedIndex < 0 || _rooms.SelectedIndex >= _found.Count)
            { _status.Text = T("FindFirst"); return; }
            await JoinAsync(_found[_rooms.SelectedIndex]);
        };
        _cancel.Text = language["Library.Cancel"];
        LibraryLayout.Quiet(_cancel);
        _cancel.Clicked += (_, _) => { _searchCancellation?.Cancel(); _operation?.Cancel(); };
        _body.Add(LibraryLayout.Muted(T("SameWifi")));
        LibraryLayout.Styled(_status, "LibraryMuted"); _body.Add(_status);
        var room = new VerticalStackLayout { Spacing = 10 };
        room.Add(LibraryLayout.Muted(T("RoomCode")));
        var controls = new Grid { ColumnDefinitions = { new(new GridLength(96)), new(GridLength.Star) }, ColumnSpacing = 12 };
        controls.Add(_code); controls.Add(refresh, 1); room.Add(controls); room.Add(_cancel);
        _senderAddress.Placeholder = T("SenderAddressHint");
        var direct = LibraryLayout.Quiet(new Button { Text = T("DirectAddressOption") });
        direct.Clicked += (_, _) => _senderAddress.IsVisible = !_senderAddress.IsVisible;
        room.Add(direct); room.Add(_senderAddress);
        _body.Add(LibraryLayout.Surface(room, new Thickness(16)));
        _matches.Add(LibraryLayout.Muted(T("MatchingRooms")));
        _matches.Add(_rooms); _matches.Add(join);
        _matchesSurface = LibraryLayout.Surface(_matches, new Thickness(16));
        _matchesSurface.IsVisible = false; _body.Add(_matchesSurface);
        Content = new ScrollView { Content = _body };
    }

    private async Task SearchAsync()
    {
        if (_busy || _searching) return;
        if (!LanRoomDiscovery.IsValidCode(_code.Text)) { _status.Text = T("InvalidRoomCode"); return; }
        if (!TrySenderAddress(out _)) return;
        var requestedCode = _code.Text;
        _searching = true; _searchCancellation = new CancellationTokenSource();
        _cancel.IsVisible = true;
        _code.IsReadOnly = true;
        LanDiscoveredRoom? automatic = null;
        try
        {
            using var discovery = LanDiscoveryAccess.Hold();
            _status.Text = T("FindingRoom");
            while (true)
            {
                if (!TrySenderAddress(out var directAddress)) return;
                _found = await LanRoomDiscovery.FindAsync(requestedCode, _searchCancellation.Token,
                    directAddress is null ? null : [directAddress]);
                if (_found.Count > 0) break;
                _senderAddress.IsVisible = true;
                _status.Text = T("WaitingForRoom");
                await Task.Delay(600, _searchCancellation.Token);
            }
            _rooms.ItemsSource = _found.Select(room => room.Name + " · " + room.Invite.Host).ToArray();
            _rooms.SelectedIndex = _found.Count == 1 ? 0 : -1;
            if (_matchesSurface is not null) _matchesSurface.IsVisible = _found.Count > 1;
            if (_found.Count == 1) automatic = _found[0];
            else _status.Text = T("ChooseSender");
        }
        catch (OperationCanceledException) { _status.Text = T("Cancelled"); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = T("Failed"); }
        finally
        {
            _searchCancellation.Dispose(); _searchCancellation = null; _searching = false;
            _code.IsReadOnly = false;
            _cancel.IsVisible = false;
        }
        if (automatic is not null) await JoinAsync(automatic);
    }

    private bool TrySenderAddress(out IPAddress? address)
    {
        address = null;
        if (!_senderAddress.IsVisible || string.IsNullOrWhiteSpace(_senderAddress.Text)) return true;
        if (IPAddress.TryParse(_senderAddress.Text.Trim(), out address) && LanAddresses.IsPrivate(address))
            return true;
        _status.Text = T("InvalidSenderAddress");
        return false;
    }

    private async Task JoinAsync(LanDiscoveredRoom selected)
    {
        if (_busy || _searching) return;
        _busy = true; _operation = new CancellationTokenSource();
        _cancel.IsVisible = true;
        _code.IsReadOnly = true;
        var token = _operation.Token;
        var path = Path.Combine(FileSystem.CacheDirectory, "lan-" + Guid.NewGuid().ToString("N") + ".hanpack");
        LanShareReceiver? receiver = null;
        var received = false;
        ContentImportResult? committed = null;
        try
        {
            _status.Text = T("Connecting") + " " + selected.Name + " · " + selected.Invite.Host;
            receiver = await LanShareReceiver.ConnectAsync(selected.Invite, DeviceInfo.Name, token);
            _status.Text = T("WaitingForSender");
            LanReceivedPackage package;
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                package = await receiver.ReceiveAsync(output, new Progress<long>(bytes =>
                    _status.Text = string.Format(T("Receiving"), bytes / 1024.0)), token);
            received = true;
            ContentImportPlan plan;
            await using (var input = File.OpenRead(path))
                plan = await _services.GetRequiredService<ContentPackageImportStore>().PlanAsync(input, token);
            var placement = await ChooseWordCategoriesAsync(plan, package.AudioSettings.WordCategories);
            if (placement is null)
            {
                await receiver.CompleteAsync(false, T("Cancelled"), token);
                _status.Text = T("Cancelled"); return;
            }
            var summary = selected.Name + " · " + selected.Invite.Host + "\n" +
                string.Format(T("ImportPreview"), plan.Added, plan.Reused, plan.AudioAdded,
                package.AudioSettings.IncludedRecordings, package.AudioSettings.Engine)
                + "\n" + string.Join("\n", plan.Titles.Take(10));
            if (!await DisplayAlertAsync(T("ReceiveTitle"), summary, T("Import"), _language["Library.Cancel"]))
            {
                await receiver.CompleteAsync(false, T("Cancelled"), token);
                _status.Text = T("Cancelled"); return;
            }
            committed = placement.Value.Destinations.Count == 0
                ? await _services.GetRequiredService<ContentPackageImportStore>().CommitAsync(plan, token)
                : await _services.GetRequiredService<WordCategoryTransferStore>().CommitAsync(plan,
                    placement.Value.Destinations, placement.Value.Created, token);
            await receiver.CompleteAsync(true, token: token);
            _status.Text = string.Format(T("Imported"), committed.Added, committed.Reused);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (received && committed is null && receiver is not null) await RejectAsync(receiver, T("Cancelled"));
            _status.Text = committed is null ? T("Cancelled") : ImportedWithReceiptWarning(committed);
        }
        catch (OperationCanceledException error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            _status.Text = receiver is null ? T("ConnectTimeout") : T("Failed");
        }
        catch (PackageException error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            if (received && committed is null && receiver is not null) await RejectAsync(receiver, T("InvalidPackage"));
            _status.Text = committed is null ? T("InvalidPackage") : ImportedWithReceiptWarning(committed);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            if (received && committed is null && receiver is not null) await RejectAsync(receiver, T("Failed"));
            _status.Text = committed is not null ? ImportedWithReceiptWarning(committed) : receiver is null ? error switch
            {
                SocketException => T("ConnectFailed"),
                AuthenticationException => T("SecureConnectFailed"),
                InvalidDataException => T("SecureConnectFailed"),
                IOException => T("SecureConnectFailed"),
                _ => T("Failed")
            } : T("Failed");
        }
        finally
        {
            if (receiver is not null) await receiver.DisposeAsync();
            _operation.Dispose(); _operation = null; _busy = false; _code.IsReadOnly = false;
            _cancel.IsVisible = false;
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    private string ImportedWithReceiptWarning(ContentImportResult result) =>
        string.Format(T("Imported"), result.Added, result.Reused) + " " + T("ReceiptUnknown");

    private async Task<(Dictionary<string, string> Destinations, List<CustomWordCategory> Created)?>
        ChooseWordCategoriesAsync(ContentImportPlan plan, IReadOnlyList<LanWordCategory>? transmitted)
    {
        var sources = WordCategoryTransferStore.SourceCategories(plan);
        if (transmitted is not null && (sources.Count != transmitted.Count ||
            sources.Any(id => !transmitted.Any(x => x.Id == id))))
            throw new InvalidDataException("LAN_CATEGORY_MISMATCH");
        var sourceNames = transmitted?.ToDictionary(x => x.Id, x => x.Name) ?? [];
        var store = _services.GetRequiredService<CustomWordCategoryStore>();
        var existing = (await store.ListAsync()).ToList();
        var preferences = (await store.ListBuiltInAsync()).ToDictionary(x => x.Id, x => x.Name);
        var created = new List<CustomWordCategory>();
        var destinations = new Dictionary<string, string>(StringComparer.Ordinal);
        string Name(string id) => existing.Concat(created).FirstOrDefault(x => x.Id == id)?.Name ??
            preferences.GetValueOrDefault(id) ?? (id == WordCategories.Other || WordCategories.All.Contains(id)
                ? _language["WordCategories." + id] : id);
        foreach (var source in sources)
        {
            var sourceName = sourceNames.GetValueOrDefault(source) ?? Name(source);
            var targets = WordCategories.All.Append(WordCategories.Other).Concat(existing.Select(x => x.Id))
                .Concat(created.Select(x => x.Id)).ToArray();
            var options = targets.Select(id => T("PlaceIn") + " " + Name(id) + " · " + id).ToArray();
            var createOption = T("CreateCategory");
            var action = await DisplayActionSheetAsync(string.Format(T("ChooseCategory"), sourceName),
                _language["Library.Cancel"], null, options.Append(createOption).ToArray());
            if (action is null || action == _language["Library.Cancel"]) return null;
            if (action == createOption)
            {
                var name = await DisplayPromptAsync(T("CreateCategory"), _language["WordCategories.Name"],
                    _language["Library.Save"], _language["Library.Cancel"], initialValue: sourceName, maxLength: 40);
                if (name is null) return null;
                name = name.Trim();
                if (name.Length is < 1 or > 40 || name.Any(char.IsControl) ||
                    existing.Concat(created).Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    await DisplayAlertAsync(T("ReceiveTitle"), T("CategoryNameInvalid"), _language["Library.Cancel"]);
                    return null;
                }
                var category = new CustomWordCategory("custom-" + Guid.NewGuid().ToString("N"), name);
                created.Add(category); destinations[source] = category.Id;
            }
            else
            {
                var index = Array.IndexOf(options, action);
                if (index < 0) return null;
                destinations[source] = targets[index];
            }
        }
        return (destinations, created);
    }

    private static async Task RejectAsync(LanShareReceiver receiver, string detail)
    {
        try { await receiver.CompleteAsync(false, detail, CancellationToken.None); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
    }

    protected override void OnDisappearing()
    {
        _searchCancellation?.Cancel(); _operation?.Cancel();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_searching) return base.OnBackButtonPressed();
        _searchCancellation?.Cancel(); _operation?.Cancel(); return true;
    }
}
