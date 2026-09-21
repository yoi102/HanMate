using HanMate.App.Localization;
using HanMate.Core.Localization;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LocalizationService _localization;
    private readonly TextResourceInstaller _installer;
    private readonly HanMate.Infrastructure.Database.ResourceManagementStore _management;
    private readonly HanMate.Infrastructure.Database.ResourceStateStore _states;
    private readonly HanMate.Infrastructure.Catalog.BundledResourceCatalog _catalog;
    private bool _openingResources;

    public SettingsPage(LocalizationService localization, TextResourceInstaller installer,
        HanMate.Infrastructure.Database.ResourceManagementStore management, HanMate.Infrastructure.Database.ResourceStateStore states,
        HanMate.Infrastructure.Catalog.BundledResourceCatalog catalog)
    {
        InitializeComponent();
        _localization = localization;
        _installer = installer;
        _management = management; _states = states; _catalog = catalog;
        BindingContext = localization;
    }

    private async void OnResourcesClicked(object? sender, EventArgs e) => await OpenResources(null);
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
          new Label { Text = _localization["Voice.Privacy"] },
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
