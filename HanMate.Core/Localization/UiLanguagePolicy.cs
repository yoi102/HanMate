namespace HanMate.Core.Localization;

public enum UiLanguage
{
    ChineseSimplified,
    Japanese,
    English
}

public static class UiLanguagePolicy
{
    public static UiLanguage ResolveSystemLanguage(string? cultureName)
    {
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return UiLanguage.ChineseSimplified;
            if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return UiLanguage.Japanese;
        }

        return UiLanguage.English;
    }

    public static UiLanguage ParsePersisted(string value) => value switch
    {
        "zh-Hans" => UiLanguage.ChineseSimplified,
        "ja" => UiLanguage.Japanese,
        "en" => UiLanguage.English,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported UI language.")
    };

    public static string ToCultureName(UiLanguage language) => language switch
    {
        UiLanguage.ChineseSimplified => "zh-Hans",
        UiLanguage.Japanese => "ja",
        UiLanguage.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    public static string? SelectAuxiliaryTranslation(
        IReadOnlyDictionary<string, string> translations,
        UiLanguage language)
    {
        ArgumentNullException.ThrowIfNull(translations);
        if (language == UiLanguage.ChineseSimplified) return null;
        var key = ToCultureName(language);
        return translations.TryGetValue(key, out var translation) && !string.IsNullOrWhiteSpace(translation)
            ? translation
            : null;
    }
}
