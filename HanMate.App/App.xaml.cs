using Microsoft.Extensions.DependencyInjection;

using HanMate.App.Localization;

namespace HanMate.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var localization = _services.GetRequiredService<LocalizationService>();
        _ = localization.InitializeAsync();

        // Resolve the visual tree only after InitializeComponent has loaded the
        // application resource dictionaries used by the pages.
        var window = new Window(_services.GetRequiredService<AppShell>());
        var playback = _services.GetRequiredService<HanMate.Core.Audio.PlaybackCoordinator>();
        window.Stopped += async (_, _) => { await playback.StopAsync(); await Audio.OfflineVoiceSynthesizer.ReleaseCacheAsync(); };
        window.Destroying += async (_, _) => { await playback.StopAsync(); await Audio.OfflineVoiceSynthesizer.ReleaseCacheAsync(); };
        return window;
    }
}
