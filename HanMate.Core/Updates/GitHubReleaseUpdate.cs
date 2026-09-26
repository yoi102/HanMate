using System.Text.Json;

namespace HanMate.Core.Updates;

public enum UpdatePlatform { Android, Windows }

public sealed record GitHubReleaseUpdate(Version Version, Uri ReleasePage);

public static class GitHubReleaseUpdatePolicy
{
    public const string ReleasesApi = "https://api.github.com/repos/yoi102/HanMate/releases?per_page=30";
    private const string ReleasePagePrefix = "https://github.com/yoi102/HanMate/releases/tag/";

    public static GitHubReleaseUpdate? FindNewer(string json, Version installedVersion, UpdatePlatform platform)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a release list.");

        GitHubReleaseUpdate? newest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String)
                continue;

            var tag = tagValue.GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag.AsSpan(1), out var version) ||
                version <= installedVersion || (newest is not null && version <= newest.Version))
                continue;

            var expectedAsset = platform == UpdatePlatform.Android
                ? $"HanMate-{version}-android-arm64-dev-signed.apk"
                : $"HanMate-{version}-windows-x64-preview.zip";
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                !assets.EnumerateArray().Any(asset => asset.ValueKind == JsonValueKind.Object &&
                    asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                    string.Equals(name.GetString(), expectedAsset, StringComparison.Ordinal)))
                continue;

            if (!release.TryGetProperty("html_url", out var pageValue) || pageValue.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(pageValue.GetString(), UriKind.Absolute, out var page) ||
                !string.Equals(page.AbsoluteUri, ReleasePagePrefix + tag, StringComparison.Ordinal))
                continue;

            newest = new GitHubReleaseUpdate(version, page);
        }

        return newest;
    }
}
