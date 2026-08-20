using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MyVNC.App.Interop;

/// <summary>Fires a callback whenever the values under an HKCU key change.
///
/// The theme and accent color both live in the registry, and Windows normally announces a change
/// to them by broadcasting WM_SETTINGCHANGE — which is what SystemEvents surfaces. That broadcast
/// is the Settings app's doing, though, not the registry's: a value written any other way (a
/// script, a theme-switcher utility, Group Policy) changes the theme with nothing announcing it.
/// This watcher is the backstop for exactly that case.</summary>
internal sealed class RegistryWatcher : IDisposable
{
    private const int KeyNotifyLastValue = 0x00000004; // REG_NOTIFY_CHANGE_LAST_SET
    private const int KeyRead = 0x20019;
    private static readonly IntPtr HkeyCurrentUser = new(unchecked((int)0x80000001));

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int rights, out IntPtr result);

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(IntPtr key, bool watchSubtree, int filter, SafeWaitHandle e, bool asynchronous);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr key);

    private readonly ManualResetEvent _changed = new(false);
    private readonly ManualResetEvent _stopping = new(false);
    private IntPtr _key;

    private RegistryWatcher(IntPtr key, Action onChanged)
    {
        _key = key;
        var thread = new Thread(() => Watch(onChanged)) { IsBackground = true, Name = "RegistryWatcher" };
        thread.Start();
    }

    /// <summary>Starts watching an HKCU subkey, or returns null if it can't be opened — the
    /// caller is expected to treat the watcher as an optional extra, not a requirement.</summary>
    public static RegistryWatcher? Start(string subKey, Action onChanged)
    {
        try
        {
            if (RegOpenKeyEx(HkeyCurrentUser, subKey, 0, KeyRead, out var key) != 0) return null;
            return new RegistryWatcher(key, onChanged);
        }
        catch
        {
            return null;
        }
    }

    private void Watch(Action onChanged)
    {
        WaitHandle[] handles = [_changed, _stopping];
        while (true)
        {
            // Each notification is a one-shot: re-arm before every wait.
            if (RegNotifyChangeKeyValue(_key, false, KeyNotifyLastValue, _changed.SafeWaitHandle, true) != 0) return;
            if (WaitHandle.WaitAny(handles) != 0) return; // _stopping won
            _changed.Reset();
            // Closing the key also signals the event, so re-check: a Dispose racing the wait must
            // not look like a real change and fire the callback on the way out.
            if (_stopping.WaitOne(0)) return;
            onChanged();
        }
    }

    public void Dispose()
    {
        _stopping.Set();
        if (_key != IntPtr.Zero)
        {
            RegCloseKey(_key);
            _key = IntPtr.Zero;
        }
    }
}
