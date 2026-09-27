namespace HanMate.App.Appearance;

internal static class ThemePreferences
{
    private const string PreferenceKey = "appearance.theme.v1";

    public static AppTheme Current => Preferences.Default.Get(PreferenceKey, "system") switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.Unspecified
    };

    public static void Select(AppTheme theme)
    {
        var value = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            AppTheme.Unspecified => "system",
            _ => throw new ArgumentOutOfRangeException(nameof(theme))
        };
        Preferences.Default.Set(PreferenceKey, value);
        if (Application.Current is { } app) app.UserAppTheme = theme;
    }
}
