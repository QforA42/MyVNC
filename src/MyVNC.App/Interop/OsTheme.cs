using Microsoft.Win32;
using MyVNC.App.Models;

namespace MyVNC.App.Interop;

/// <summary>Reads the current Windows "app mode" (Settings → Personalization → Colors) so the
/// app matches the OS instead of asking the user to pick a theme separately.</summary>
internal static class OsTheme
{
    public static AppTheme Detect()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch { /* registry key missing/inaccessible — fall back to dark */ }
        return AppTheme.Dark;
    }
}
