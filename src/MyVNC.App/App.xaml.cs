using System.Windows;
using MyVNC.App.Models;
using MyVNC.App.Services;
using MyVNC.Rfb;

namespace MyVNC.App;

public partial class App : Application
{
    public static SettingsStore SettingsStore { get; } = new();
    public static AppSettings Settings { get; private set; } = new();
    public static AppTheme ResolvedTheme { get; private set; } = AppTheme.Dark;

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logged only when Settings.DebugLogging is on (see AppLog) — catches exactly the
        // moments a problem is otherwise impossible to diagnose without hands-on access: an
        // unhandled crash on the UI thread, a background thread, or an unobserved Task fault.
        DispatcherUnhandledException += (_, args) => AppLog.WriteException("Unhandled UI exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) AppLog.WriteException("Unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => AppLog.WriteException("Unobserved task exception", args.Exception);
        RfbLog.Sink = AppLog.Write;

        // Only one MyVNC process may run at a time — a second launch (desktop icon, a taskbar
        // jump-list shortcut) would otherwise get its own empty SessionWindow.Shared and could
        // never join the already-running window's tabs, no matter what Settings.SessionOpenMode
        // says. The second process forwards its args to the first and exits immediately.
        //
        // "Global\" (not "Local\") because the mutex must be visible across logon
        // sessions/integrity levels too — a "Local\" mutex is isolated per session, so a launch
        // from a differently-elevated or differently-sessioned context could pass the "am I
        // first?" check even though a real instance is already running, and both would then
        // fight over the same named pipe in SingleInstance.ListenLoop with no backoff, spinning
        // into a multi-GB-in-seconds runaway — exactly what happened here. Falls back to
        // session-local scope if creating a global kernel object is ever denied.
        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\MyVNC-SingleInstance", out isFirstInstance);
        }
        catch (UnauthorizedAccessException)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\MyVNC-SingleInstance", out isFirstInstance);
        }
        if (!isFirstInstance)
        {
            SingleInstance.ForwardToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();
        AppLog.Enabled = Settings.DebugLogging;
        Loc.SetLanguage(Settings.Language);
        ResolvedTheme = Interop.OsTheme.Detect();
        ApplyTheme(ResolvedTheme);

        // Launched from the taskbar jump list ("--connect <profileId>") — see JumpListBuilder.
        string? autoConnectId = null;
        var connectFlagIndex = Array.IndexOf(e.Args, "--connect");
        if (connectFlagIndex >= 0 && connectFlagIndex + 1 < e.Args.Length)
            autoConnectId = e.Args[connectFlagIndex + 1];

        ResourceWatchdog.Start();

        new MainWindow(openSettings: false, autoConnectId).Show();

        // MyVNC.App.MainWindow.Current, not a captured reference — a language change replaces
        // the whole window (see MainWindow.OnLanguageChanged), and activation must reach
        // whichever instance is actually alive when it arrives. (Fully qualified because
        // Application.MainWindow — the inherited property — would otherwise shadow our class.)
        SingleInstance.StartListening(args => Dispatcher.Invoke(() => MyVNC.App.MainWindow.Current?.HandleActivation(args)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Colors.Dark/Light.xaml supplies every brush ControlStyles.xaml's templates
    /// reference via StaticResource — those resolve at the moment each Style/Setter is parsed,
    /// so the colors dictionary must be merged in first. The OS theme is read once at launch;
    /// if the user flips Windows' own light/dark setting, the app picks it up next time it starts.</summary>
    private static void ApplyTheme(AppTheme theme)
    {
        var colorsSource = theme == AppTheme.Light ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";
        Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(colorsSource, UriKind.Relative) });
        Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/ControlStyles.xaml", UriKind.Relative) });
    }
}
