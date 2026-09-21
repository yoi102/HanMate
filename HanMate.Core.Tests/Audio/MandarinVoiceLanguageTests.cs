using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class MandarinVoiceLanguageTests
{
    [Theory]
    [InlineData("zh")] [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    [InlineData("zh_CN")] [InlineData("cmn-Hans-CN")]
    public void AcceptsMandarinVoicesWithoutCountry(string language) => Assert.True(MandarinVoiceLanguage.Matches(language));

    [Theory]
    [InlineData("yue-HK")] [InlineData("zh-HK")] [InlineData("en-US")] [InlineData(null)]
    public void DoesNotMislabelOtherLanguages(string? language) => Assert.False(MandarinVoiceLanguage.Matches(language));
}
