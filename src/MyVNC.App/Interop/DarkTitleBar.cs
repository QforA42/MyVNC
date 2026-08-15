using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MyVNC.App.Interop;

/// <summary>Toggles the Windows 10/11 dark/light title bar to match whichever theme the app
/// resolved from the OS at launch (see <see cref="OsTheme"/>).</summary>
internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        void TrySet()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int enabled = App.ResolvedTheme == Models.AppTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        if (window.IsLoaded) TrySet();
        else window.SourceInitialized += (_, _) => TrySet();
    }
}
