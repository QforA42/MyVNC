using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MyVNC.App.Interop;

/// <summary>
/// Notifies whenever the Windows clipboard changes, using WM_CLIPBOARDUPDATE — this fires the
/// instant any app copies text, so remote clipboard sync doesn't depend on the user clicking
/// back into the VNC view first.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly HwndSource _source;

    public event Action? ClipboardChanged;

    public ClipboardMonitor(Window window)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)!;
        AddClipboardFormatListener(_source.Handle);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            ClipboardChanged?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
    }
}
