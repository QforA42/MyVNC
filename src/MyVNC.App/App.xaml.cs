using System.Windows;
using MyVNC.App.Models;
using MyVNC.App.Services;

namespace MyVNC.App;

public partial class App : Application
{
    public static SettingsStore SettingsStore { get; } = new();
    public static AppSettings Settings { get; private set; } = new();
    public static AppTheme ResolvedTheme { get; private set; } = AppTheme.Dark;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsStore.Load();
        Loc.SetLanguage(Settings.Language);
        ResolvedTheme = Interop.OsTheme.Detect();
        ApplyTheme(ResolvedTheme);

        // Launched from the taskbar jump list ("--connect <profileId>") — see JumpListBuilder.
        string? autoConnectId = null;
        var connectFlagIndex = Array.IndexOf(e.Args, "--connect");
        if (connectFlagIndex >= 0 && connectFlagIndex + 1 < e.Args.Length)
            autoConnectId = e.Args[connectFlagIndex + 1];

        new MainWindow(openSettings: false, autoConnectId).Show();
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
