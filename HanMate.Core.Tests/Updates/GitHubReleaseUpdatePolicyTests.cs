using HanMate.Core.Updates;

namespace HanMate.Core.Tests.Updates;

public class GitHubReleaseUpdatePolicyTests
{
    private const string Releases = """
        [
          { "tag_name": "v0.1.4", "draft": false,
            "html_url": "https://github.com/yoi102/HanMate/releases/tag/v0.1.4",
            "assets": [{ "name": "HanMate-0.1.4-android-arm64-dev-signed.apk" }] },
          { "tag_name": "v0.1.5", "draft": true,
            "html_url": "https://github.com/yoi102/HanMate/releases/tag/v0.1.5",
            "assets": [{ "name": "HanMate-0.1.5-android-arm64-dev-signed.apk" }] },
          { "tag_name": "v0.1.6", "draft": false,
            "html_url": "https://evil.example/releases/tag/v0.1.6",
            "assets": [{ "name": "HanMate-0.1.6-android-arm64-dev-signed.apk" }] },
          { "tag_name": "v0.1.2", "draft": false,
            "html_url": "https://github.com/yoi102/HanMate/releases/tag/v0.1.2",
            "assets": [{ "name": "HanMate-0.1.2-windows-x64-preview.zip" }] }
        ]
        """;

    [Fact]
    public void AcceptsNewerPreviewOnlyWhenTrustedPageAndPlatformAssetExist()
    {
        var update = GitHubReleaseUpdatePolicy.FindNewer(Releases, new Version(0, 1, 3), UpdatePlatform.Android);
        Assert.Equal(new Version(0, 1, 4), update?.Version);
        Assert.Equal("https://github.com/yoi102/HanMate/releases/tag/v0.1.4", update?.ReleasePage.AbsoluteUri);
    }

    [Fact]
    public void DoesNotOfferOlderOrWrongPlatformAssets()
    {
        Assert.Null(GitHubReleaseUpdatePolicy.FindNewer(Releases, new Version(0, 1, 3), UpdatePlatform.Windows));
        Assert.Null(GitHubReleaseUpdatePolicy.FindNewer(Releases, new Version(0, 1, 4), UpdatePlatform.Android));
    }

    [Fact]
    public void SelectsHighestVersionRatherThanApiOrder()
    {
        const string releases = """
            [
              {"tag_name":"v0.1.4","draft":false,"html_url":"https://github.com/yoi102/HanMate/releases/tag/v0.1.4","assets":[{"name":"HanMate-0.1.4-windows-x64-preview.zip"}]},
              {"tag_name":"v0.1.7","draft":false,"html_url":"https://github.com/yoi102/HanMate/releases/tag/v0.1.7","assets":[{"name":"HanMate-0.1.7-windows-x64-preview.zip"}]}
            ]
            """;
        Assert.Equal(new Version(0, 1, 7), GitHubReleaseUpdatePolicy.FindNewer(releases, new Version(0, 1, 3), UpdatePlatform.Windows)?.Version);
    }

}
