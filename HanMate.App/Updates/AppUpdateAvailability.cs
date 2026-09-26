using HanMate.Core.Updates;

namespace HanMate.App.Updates;

public sealed class AppUpdateAvailability
{
    private const string AvailableVersionKey = "updates.availableVersion";
    private GitHubReleaseUpdate? _available;

    public AppUpdateAvailability()
    {
        var saved = Preferences.Default.Get(AvailableVersionKey, string.Empty);
        var installed = AppUpdateChecker.InstalledVersion;
        if (Version.TryParse(saved, out var version) && installed is not null && version > installed)
            _available = new GitHubReleaseUpdate(version,
                new Uri($"https://github.com/yoi102/HanMate/releases/tag/v{version}"));
        else if (!string.IsNullOrEmpty(saved)) Preferences.Default.Remove(AvailableVersionKey);
    }

    public event EventHandler? Changed;
    public GitHubReleaseUpdate? Available => _available;
    public bool HasUpdate => _available is not null;

    public void SetAvailable(GitHubReleaseUpdate? available)
    {
        if (Equals(_available, available)) return;
        _available = available;
        if (available is null) Preferences.Default.Remove(AvailableVersionKey);
        else Preferences.Default.Set(AvailableVersionKey, available.Version.ToString());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
