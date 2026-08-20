namespace MyVNC.App.Models;

/// <summary>What the user chose in Settings → theme. Distinct from <see cref="AppTheme"/>, which
/// is the palette actually in use: "System" resolves to one or the other depending on the Windows
/// app mode at the time, and follows it when it changes.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public static class ThemePreferenceExtensions
{
    public static string LocKey(this ThemePreference preference) => preference switch
    {
        ThemePreference.Light => "Theme.Light",
        ThemePreference.Dark => "Theme.Dark",
        _ => "Theme.System",
    };
}
