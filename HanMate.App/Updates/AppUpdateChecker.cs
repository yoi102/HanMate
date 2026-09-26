using System.Net.Http.Headers;
using HanMate.Core.Updates;

namespace HanMate.App.Updates;

internal static class AppUpdateChecker
{
    public static UpdatePlatform? CurrentPlatform => DeviceInfo.Current.Platform == DevicePlatform.Android
        ? UpdatePlatform.Android
        : DeviceInfo.Current.Platform == DevicePlatform.WinUI ? UpdatePlatform.Windows : null;

    public static Version? InstalledVersion => Version.TryParse(AppInfo.Current.VersionString, out var version)
        ? version : null;

    public static async Task<GitHubReleaseUpdate?> CheckAsync(Version installedVersion, UpdatePlatform platform)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HanMate", installedVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(GitHubReleaseUpdatePolicy.ReleasesApi).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return GitHubReleaseUpdatePolicy.FindNewer(json, installedVersion, platform);
    }

}
