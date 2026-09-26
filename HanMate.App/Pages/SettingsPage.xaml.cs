using HanMate.App.Localization;
using HanMate.Core.Localization;
using HanMate.Infrastructure.Packages;
using HanMate.App.Updates;
using HanMate.Core.Updates;

namespace HanMate.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LocalizationService _localization;
    private readonly TextResourceInstaller _installer;
    private readonly HanMate.Infrastructure.Database.ResourceManagementStore _management;
    private readonly HanMate.Infrastructure.Database.ResourceStateStore _states;
    private readonly HanMate.Infrastructure.Catalog.BundledResourceCatalog _catalog;
    private readonly AppUpdateAvailability _updates;
    private bool _openingResources;
    private bool _checkingUpdates;

    public SettingsPage(LocalizationService localization, TextResourceInstaller installer,
        HanMate.Infrastructure.Database.ResourceManagementStore management, HanMate.Infrastructure.Database.ResourceStateStore states,
        HanMate.Infrastructure.Catalog.BundledResourceCatalog catalog, AppUpdateAvailability updates)
    {
        InitializeComponent();
        _localization = localization;
        _installer = installer;
        _management = management; _states = states; _catalog = catalog;
        _updates = updates;
        BindingContext = localization;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _updates.Changed += OnUpdateChanged;
        RefreshUpdateIndicator();
    }

    protected override void OnDisappearing()
    {
        _updates.Changed -= OnUpdateChanged;
        base.OnDisappearing();
    }

    private void OnUpdateChanged(object? sender, EventArgs e) => RefreshUpdateIndicator();

    private void RefreshUpdateIndicator()
    {
        UpdateTile.ShowBadge = _updates.HasUpdate;
        UpdateTile.Detail = _updates.HasUpdate ? _localization["Update.AvailableHint"] : string.Empty;
        ViewReleaseButton.IsVisible = _updates.HasUpdate;
        if (_updates.Available is { } available && !_checkingUpdates)
        {
            UpdateStatusLabel.Text = string.Format(_localization["Update.Available"], available.Version);
            UpdateStatusLabel.IsVisible = true;
        }
        else if (!_checkingUpdates) UpdateStatusLabel.IsVisible = false;
    }

    private async void OnResourcesClicked(object? sender, EventArgs e) => await OpenResources(null);
    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        UpdateStatusLabel.IsVisible = true;
        UpdateStatusLabel.Text = _localization["Update.Checking"];
        try
        {
            UpdatePlatform? platform = AppUpdateChecker.CurrentPlatform;
            if (platform is null)
            {
                UpdateStatusLabel.Text = _localization["Update.Unsupported"];
                return;
            }

            var installedVersion = AppUpdateChecker.InstalledVersion;
            if (installedVersion is null)
            {
                UpdateStatusLabel.Text = _localization["Update.Unavailable"];
                return;
            }

            var available = await AppUpdateChecker.CheckAsync(installedVersion, platform.Value);
            _updates.SetAvailable(available);
            if (available is null)
            {
                UpdateStatusLabel.Text = string.Format(_localization["Update.Current"], installedVersion);
                return;
            }

            UpdateStatusLabel.Text = string.Format(_localization["Update.Available"], available.Version);
        }
        catch (Exception)
        {
            UpdateStatusLabel.Text = _localization["Update.Unavailable"];
        }
        finally { _checkingUpdates = false; }
    }
    private async void OnViewReleaseClicked(object? sender, EventArgs e)
    {
        if (_updates.Available is not { } available) return;
        try { await Launcher.Default.OpenAsync(available.ReleasePage); }
        catch { UpdateStatusLabel.Text = _localization["Update.OpenFailed"]; UpdateStatusLabel.IsVisible = true; }
    }
    private async void OnPinyinResourcesClicked(object? sender, EventArgs e)
    {
        if (_openingResources || Handler?.MauiContext?.Services is not { } services) return;
        _openingResources = true;
        try { await Navigation.PushAsync(new PinyinResourcesPage(_localization, services.GetRequiredService<HanMate.App.Audio.BundledPronunciationService>(), services.GetRequiredService<HanMate.Infrastructure.Database.TeachingCatalogStore>())); }
        finally { _openingResources = false; }
    }
    private async void OnSpeechClicked(object? sender, EventArgs e)
    {
        if (_openingResources || Handler?.MauiContext?.Services is not { } services) return;
        _openingResources = true;
        try { await Navigation.PushAsync(ActivatorUtilities.CreateInstance<SpeechSettingsPage>(services)); }
        finally { _openingResources = false; }
    }
    private async void OnVoicePacksClicked(object? sender, EventArgs e)
    {
        if (_openingResources || Handler?.MauiContext?.Services is not { } services) return;
        _openingResources = true;
        try { await Navigation.PushAsync(ActivatorUtilities.CreateInstance<SpeechSettingsPage>(services)); }
        finally { _openingResources = false; }
    }
    private async void OnPrivacyClicked(object? sender, EventArgs e)
    {
        if (_openingResources) return;
        _openingResources = true;
        try { await Navigation.PushAsync(new ContentPage { Title = _localization["Privacy.Title"], Content = new ScrollView
        { Content = new VerticalStackLayout { Padding = 20, Spacing = 16, Children =
        { new Label { Text = _localization["Privacy.Local"] }, new Label { Text = _localization["Privacy.Speech"] },
          new Label { Text = _localization["Voice.Privacy"] }, new Label { Text = _localization["Privacy.Updates"] },
          new Label { Text = _localization["Privacy.Export"] }, new Label { Text = _localization["Privacy.Delete"] } } } } }); }
        finally { _openingResources = false; }
    }
    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        if (_openingResources || Handler?.MauiContext?.Services is not { } services) return;
        _openingResources = true;
        try { await Navigation.PushAsync(new BackupPage(services.GetRequiredService<BackupExportStore>(), services.GetRequiredService<BackupImportStore>(), services.GetRequiredService<ShareFileStore>(), _localization, services.GetRequiredService<BackupReplacementStore>(), services.GetRequiredService<HanMate.Core.Audio.PlaybackCoordinator>(), services.GetRequiredService<SafetyRecoveryStore>(), services.GetRequiredService<OrphanAudioStore>())); }
        finally { _openingResources = false; }
    }
    private async void OnDictionariesClicked(object? sender, EventArgs e) => await OpenResources(HanMate.Core.Resources.ResourceKind.Dictionary);
    private async Task OpenResources(HanMate.Core.Resources.ResourceKind? kind)
    {
        if (_openingResources) return;
        _openingResources = true;
        try { await Navigation.PushAsync(new ResourceManagementPage(_management, _states, _catalog, _installer, _localization, kind)); }
        finally { _openingResources = false; }
    }

    private async void OnChineseClicked(object? sender, EventArgs e) =>
        await ChangeLanguageAsync(UiLanguage.ChineseSimplified);

    private async void OnJapaneseClicked(object? sender, EventArgs e) =>
        await ChangeLanguageAsync(UiLanguage.Japanese);

    private async void OnEnglishClicked(object? sender, EventArgs e) =>
        await ChangeLanguageAsync(UiLanguage.English);

    private async Task ChangeLanguageAsync(UiLanguage language)
    {
        var saved = await _localization.ChangeLanguageAsync(language);
        SaveStatusLabel.Text = _localization[saved ? "State.Saved" : "State.Cancelled"];
        SaveStatusLabel.IsVisible = true;
    }
}
