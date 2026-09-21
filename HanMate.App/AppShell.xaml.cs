namespace HanMate.App;

public partial class AppShell : Shell
{
    private readonly Microsoft.Extensions.Logging.ILogger<AppShell> _logger;
    private bool _returningToRoot;

    public AppShell(
        Localization.LocalizationService localization,
        Pages.PinyinPage pinyinPage,
        Pages.LearningPage learningPage,
        Pages.SearchPage searchPage,
        Pages.FavoritesPage favoritesPage,
        Pages.SettingsPage settingsPage,
        HanMate.Core.Audio.PlaybackCoordinator playback,
        Microsoft.Extensions.Logging.ILogger<AppShell> logger)
    {
        _logger = logger;
        InitializeComponent();
        BindingContext = localization;
        PinyinContent.Content = pinyinPage;
        LearningContent.Content = learningPage;
        SearchContent.Content = searchPage;
        FavoritesContent.Content = favoritesPage;
        SettingsContent.Content = settingsPage;
        // Shell tab switches can keep a pushed page alive without its disappearance callback.
        // Stop at the navigation boundary as well as at page/window lifecycle boundaries.
        Navigating += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { searchPage.CancelPending(); await playback.StopAsync(); }
            finally { deferral.Complete(); }
        };
        Navigated += async (_, _) =>
        {
            if (CurrentPage == searchPage) await searchPage.RefreshIfChangedAsync();
        };
    }

    internal async Task ReturnToTabRootAsync(ShellSection section)
    {
        if (_returningToRoot || Handler is null || Navigation.ModalStack.Count != 0 ||
            section.Parent is not ShellItem item || item.Parent != this ||
            section.CurrentItem is not { } content) return;
        if (CurrentItem == item && item.CurrentItem == section && section.Navigation.NavigationStack.Count <= 1) return;

        _returningToRoot = true;
        try
        {
            // Each tab has a unique, explicit content route. Absolute Shell navigation
            // selects it and clears its stack in one guarded operation, including deferrals.
            await GoToAsync($"//{Routing.GetRoute(content)}", animate: false);
        }
        catch (Exception exception)
        {
            // A native gesture can race a pending navigation or window teardown.
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, exception,
                "Could not return tab {Route} to its root", Routing.GetRoute(content));
        }
        finally { _returningToRoot = false; }
    }
}
