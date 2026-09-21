using System.ComponentModel;
using System.Text.Json;
using HanMate.App.Localization;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public sealed class ResourceManagementPage : ContentPage
{
    private readonly ResourceManagementStore _management;
    private readonly ResourceStateStore _states;
    private readonly BundledResourceCatalog _catalog;
    private readonly TextResourceInstaller _installer;
    private readonly LocalizationService _language;
    private readonly ResourceKind? _kind;
    private readonly Guid? _resourceId;
    private readonly VerticalStackLayout _body = new() { Spacing = 12 };
    private readonly Label _status = new();
    private readonly Button _refresh = new();
    private IReadOnlyList<ManagedResource> _resources = [];
    private bool _busy, _subscribed;
    private CancellationTokenSource? _operation;
    private Shell? _shell;

    public ResourceManagementPage(ResourceManagementStore management, ResourceStateStore states, BundledResourceCatalog catalog,
        TextResourceInstaller installer, LocalizationService language, ResourceKind? kind = null, Guid? resourceId = null)
    {
        _management = management; _states = states; _catalog = catalog; _installer = installer; _language = language; _kind = kind; _resourceId = resourceId;
        _refresh.Clicked += async (_, _) => await ReloadAsync();
        var header = new VerticalStackLayout { Spacing = 8, Children = { _refresh, _status } };
        var grid = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        grid.Add(header); grid.Add(new ScrollView { Content = _body }, 0, 1); Content = grid;
        Render();
    }

    protected override async void OnAppearing() { base.OnAppearing(); await ReloadAsync(); }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null && !_subscribed)
        {
            _language.PropertyChanged += LanguageChanged; _shell = Shell.Current;
            if (_shell is not null) { _shell.Navigated += Navigated; _shell.Navigating += Navigating; }
            _subscribed = true;
        }
        else if (Handler is null && _subscribed)
        {
            _language.PropertyChanged -= LanguageChanged;
            if (_shell is not null) { _shell.Navigated -= Navigated; _shell.Navigating -= Navigating; }
            _subscribed = false; _operation?.Cancel();
        }
    }
    private void Navigating(object? sender, ShellNavigatingEventArgs e) => _operation?.Cancel();
    private async void Navigated(object? sender, ShellNavigatedEventArgs e) { if (_shell?.CurrentPage == this) await ReloadAsync(); }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName)) Render(); }
    private string T(string key) => _language["Manage." + key];

    private async Task ReloadAsync() => await RunAsync(async token =>
    {
        await Task.Run(() => _catalog.EnsureInstalledAsync(token), token);
        _resources = await Task.Run(() => _management.GetAsync(_kind, token), token);
        Render();
    });

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return; _busy = true; _body.IsEnabled = false; _refresh.IsEnabled = false;
        using var operation = new CancellationTokenSource(); _operation = operation;
        _status.Text = T("Working");
        try { await action(operation.Token); _status.Text = ""; }
        catch (OperationCanceledException) { _status.Text = _language["Install.Cancelled"]; }
        catch (PackageException e) { _status.Text = e.Code == "PLAN_STALE" ? T("Stale") : e.Code == "RESOURCE_DEPENDENCIES_EXIST" ? T("Protected") : _language["Install.Failed"]; }
        catch (DataEpochConflictException) { _status.Text = T("Stale"); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { _status.Text = T("Stale"); }
        catch (Exception) { _status.Text = _language["Install.Failed"]; }
        finally { _operation = null; _busy = false; _body.IsEnabled = true; _refresh.IsEnabled = true; }
    }

    private void Render()
    {
        Title = _kind == ResourceKind.Dictionary ? _language.ManageDictionaries : _language.ManageResources;
        _refresh.Text = T("Refresh"); _body.Clear();
        if (_resourceId is null)
        {
            if (_kind is null or ResourceKind.Dictionary)
            {
                var dictionary = Action(_language["Dictionary.About"], () => Navigation.PushAsync(new DefaultDictionaryPage(_language)));
                dictionary.AutomationId = "Manage.DefaultDictionary";
                _body.Add(new Border { Padding = 12, Content = new VerticalStackLayout { Spacing = 8, Children =
                {
                    new Label { Text = HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.Name, FontSize = 20 },
                    new Label { Text = string.Format(_language["Dictionary.Summary"], HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.EntryCount.ToString("N0"), HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.Version) }, dictionary
                } } });
            }
            var import = Action(_language["Install.Pick"], OpenInstaller); _body.Add(import);
            if (_resources.Count == 0) _body.Add(new Label { Text = T("Empty") });
            foreach (var item in _resources)
            {
                var button = Action(T("Details"), async () =>
                {
                    if (_busy) return;
                    await Navigation.PushAsync(new ResourceManagementPage(_management, _states, _catalog, _installer, _language, _kind, item.State.ResourceId));
                });
                button.AutomationId = "Manage.Resource." + item.State.ResourceId;
                _body.Add(new Border { Padding = 12, Content = new VerticalStackLayout { Spacing = 8, Children =
                {
                    new Label { Text = item.Name(_language.CurrentCultureName), FontSize = 20 },
                    new Label { Text = Summary(item), FontSize = 14 }, button
                } } });
            }
            return;
        }
        var resource = _resources.FirstOrDefault(r => r.State.ResourceId == _resourceId);
        if (resource is null) { _body.Add(new Label { Text = T("Empty") }); return; }
        var state = resource.State;
        Title = resource.Name(_language.CurrentCultureName);
        _body.Add(new Label { Text = Title, FontSize = 24 });
        _body.Add(new Label { Text = Summary(resource) });
        _body.Add(new Label { Text = string.Format(T("Size"), resource.TextBytes) });
        using var descriptor = JsonDocument.Parse(state.DescriptorJson); var data = descriptor.RootElement;
        _body.Add(new Label { Text = string.Format(T("Source"), Value("publisherName"), Value("licenseIdentifier")) });
        _body.Add(new Label { Text = Value("permissionNotes") });
        _body.Add(new Label { Text = _language["Install.Unverified"], FontSize = 13 });
        var toggle = Action(T(state.IsEnabled ? "Disable" : "Enable"), () => RunAsync(async token =>
        {
            await Task.Run(() => _states.SetEnabledAsync(state.ResourceId, !state.IsEnabled, state.RowRevision,
                new(Guid.NewGuid(), state.ResourceId, state.IsEnabled ? ResourceOperationType.Disable : ResourceOperationType.Enable,
                    resource.Epoch, DateTimeOffset.UtcNow, "{}"), token), token);
            await RefreshRows(token);
        }));
        toggle.IsEnabled = state.IsPresent; toggle.AutomationId = "Manage.Toggle"; _body.Add(toggle);
        _body.Add(new Label { Text = T("PriorityHint") });
        var priority = new Entry { Text = state.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture), Keyboard = Keyboard.Numeric,
            MaxLength = 10, AutomationId = "Manage.Priority" };
        _body.Add(priority);
        _body.Add(Action(T("SavePriority"), async () =>
        {
            if (!int.TryParse(priority.Text, out var value) || value < 0) { _status.Text = T("InvalidPriority"); return; }
            await RunAsync(async token => { await Task.Run(() => _management.SetPriorityAsync(resource, value, token), token); await RefreshRows(token); });
        }));
        if (state.IsPresent)
        {
            if (state.Distribution == ResourceDistribution.External)
                _body.Add(Action(_language["Install.UpdatePick"], () => Navigation.PushAsync(new ResourceLibraryPage(_installer, _language, expectedResource: state.ResourceId))));
            _body.Add(Action(T("Entries"), () => Navigation.PushAsync(new ResourceEntriesPage(_management, _states, state.ResourceId, _language))));
            var uninstall = Action(T("Uninstall"), () => RunAsync(async token =>
            {
                var plan = await Task.Run(() => _management.PreviewRetainingUninstallAsync(resource, token), token);
                token.ThrowIfCancellationRequested();
                if (!await DisplayAlertAsync(T("Uninstall"), string.Format(T("RetainingUninstall"), Title, plan.RetainedCount, plan.DeletedCount), T("Uninstall"), _language["Install.Cancel"])) return;
                token.ThrowIfCancellationRequested();
                await Task.Run(() => _management.UninstallRetainingAsync(plan, token), token); await RefreshRows(token);
            }));
            uninstall.AutomationId = "Manage.Uninstall"; _body.Add(uninstall);
        }
        else if (state.Distribution == ResourceDistribution.Bundled)
            _body.Add(Action(T("Restore"), () => RunAsync(async token => { await Task.Run(() => _catalog.RestoreAsync(state.ResourceId, token), token); await RefreshRows(token); })));
        else _body.Add(Action(T("ReinstallFile"), OpenInstaller));

        string Value(string key) => data.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
    }

    private async Task RefreshRows(CancellationToken token)
    { _resources = await Task.Run(() => _management.GetAsync(_kind, token), token); Render(); }
    private string Summary(ManagedResource resource) => string.Format(T("Summary"), resource.State.Version, resource.Count, resource.RemovedCount,
        T(!resource.State.IsPresent ? "Missing" : resource.State.IsEnabled ? "Enabled" : "Disabled"), resource.State.Priority);
    private async Task OpenInstaller()
    { if (!_busy) await Navigation.PushAsync(new ResourceLibraryPage(_installer, _language)); }
    private static Button Action(string text, Func<Task> action)
    { var button = new Button { Text = text }; button.Clicked += async (_, _) => await action(); return button; }
}
