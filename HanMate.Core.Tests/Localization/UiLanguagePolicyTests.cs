using HanMate.Core.Localization;

namespace HanMate.Core.Tests.Localization;

public sealed class UiLanguagePolicyTests
{
    [Theory]
    [InlineData("zh-CN", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Hant", UiLanguage.ChineseSimplified)]
    [InlineData("ja-JP", UiLanguage.Japanese)]
    [InlineData("en-US", UiLanguage.English)]
    [InlineData("fr-FR", UiLanguage.English)]
    [InlineData(null, UiLanguage.English)]
    public void ResolveSystemLanguage_UsesDocumentedMapping(string? culture, UiLanguage expected)
    {
        Assert.Equal(expected, UiLanguagePolicy.ResolveSystemLanguage(culture));
    }

    [Fact]
    public void SelectAuxiliaryTranslation_HidesChineseAndNeverFallsBackAcrossLanguages()
    {
        var translations = new Dictionary<string, string>
        {
            ["ja"] = "こんにちは",
            ["en"] = "Hello"
        };

        Assert.Null(UiLanguagePolicy.SelectAuxiliaryTranslation(translations, UiLanguage.ChineseSimplified));
        Assert.Equal("こんにちは", UiLanguagePolicy.SelectAuxiliaryTranslation(translations, UiLanguage.Japanese));
        Assert.Equal("Hello", UiLanguagePolicy.SelectAuxiliaryTranslation(translations, UiLanguage.English));
        Assert.Null(UiLanguagePolicy.SelectAuxiliaryTranslation(
            new Dictionary<string, string> { ["en"] = "Hello" }, UiLanguage.Japanese));
    }
}
