using System.Globalization;
using System.Resources;

namespace HanMate.App.Localization;

public static class AppResources
{
    private static readonly ResourceManager Manager = new("HanMate.App.Localization.AppResources", typeof(AppResources).Assembly);

    public static CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en");

    public static string Get(string key) => Manager.GetString(key, Culture) ?? $"!{key}!";
}
