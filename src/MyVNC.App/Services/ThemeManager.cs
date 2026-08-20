using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MyVNC.App.Interop;
using MyVNC.App.Models;

namespace MyVNC.App.Services;

/// <summary>Owns the live color palette: picks Colors.Dark/Light.xaml from the user's theme
/// setting (which by default follows the Windows app mode), overlays the Windows accent color on
/// top of it, and swaps the whole thing out again whenever any of that changes.
///
/// The accent always comes from Windows, including when the user has pinned light or dark — those
/// two settings are independent in Windows itself as well.
///
/// Every theme color is consumed through {DynamicResource ...} rather than {StaticResource ...}
/// precisely so this swap reaches the already-rendered UI — including a session window with live
/// RFB connections in it, which must not be torn down and rebuilt just to repaint its chrome.</summary>
internal static class ThemeManager
{
    private const string DarkPalette = "Themes/Colors.Dark.xaml";
    private const string LightPalette = "Themes/Colors.Light.xaml";

    // Where Windows keeps the two settings this class follows: the light/dark app mode and the
    // accent color.
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    private static ResourceDictionary? _palette;
    private static DispatcherTimer? _debounce;
    private static readonly List<RegistryWatcher> _watchers = [];

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Merges the initial palette and starts following the OS. Call once, at startup,
    /// before any window is created.</summary>
    public static void Initialize()
    {
        Current = Resolve();
        _palette = BuildPalette(Current);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Add(_palette);
        dictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/ControlStyles.xaml", UriKind.Relative) });

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // Belt and braces: SystemEvents only hears about a change that someone broadcast, so watch
        // the underlying keys too (see RegistryWatcher). Both paths funnel into the same debounce,
        // and a reapply that finds nothing changed is a no-op, so overlap costs nothing.
        foreach (var key in new[] { PersonalizeKey, DwmKey })
            if (RegistryWatcher.Start(key, ScheduleReapply) is { } watcher)
                _watchers.Add(watcher);
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    /// <summary>Windows raises this (category General) for WM_SETTINGCHANGE/"ImmersiveColorSet",
    /// which covers both the light/dark app mode and the accent color. It arrives on a
    /// SystemEvents worker thread, and in bursts — several notifications for one user action —
    /// so it is marshalled to the UI thread and coalesced.</summary>
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color))
            return;

        ScheduleReapply();
    }

    /// <summary>Coalesces the burst of notifications one user action produces, from either source,
    /// into a single reapply on the UI thread.</summary>
    private static void ScheduleReapply()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _debounce ??= CreateDebounceTimer();
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private static DispatcherTimer CreateDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Reapply();
        };
        return timer;
    }

    /// <summary>Rebuilds the palette and, if anything actually differs, swaps it in. Safe to call
    /// on any change of input — an unchanged palette is a no-op.</summary>
    public static void Reapply()
    {
        if (_palette is null) return;

        var theme = Resolve();
        var refreshed = BuildPalette(theme);
        if (!HasChanged(_palette, refreshed)) return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var index = dictionaries.IndexOf(_palette);
        if (index < 0) return;

        // Remove-then-insert rather than an indexer assignment: both invalidate the dynamic
        // resource references, but this keeps the palette ahead of ControlStyles.xaml in the
        // merge order, which is where every lookup expects to find it.
        dictionaries.RemoveAt(index);
        dictionaries.Insert(index, refreshed);
        _palette = refreshed;
        Current = theme;
        App.SetResolvedTheme(theme);

        // Non-client area isn't ours to repaint through resources — each window's title bar has
        // to be told about the new mode separately.
        foreach (Window window in Application.Current.Windows)
            DarkTitleBar.Apply(window);

        AppLog.Write($"Theme reapplied: {theme}, accent={Describe(refreshed, "AccentColor")}");
    }

    private static bool HasChanged(ResourceDictionary current, ResourceDictionary candidate)
    {
        foreach (var key in candidate.Keys)
        {
            if (candidate[key] is not Color color) continue;
            if (current[key] is not Color existing || existing != color) return true;
        }
        return false;
    }

    /// <summary>The palette to use right now: the user's explicit choice, or the Windows app mode
    /// when they left it on "follow Windows".</summary>
    private static AppTheme Resolve() => App.Settings.Theme switch
    {
        ThemePreference.Light => AppTheme.Light,
        ThemePreference.Dark => AppTheme.Dark,
        _ => OsTheme.Detect(),
    };

    private static ResourceDictionary BuildPalette(AppTheme theme)
    {
        var source = theme == AppTheme.Light ? LightPalette : DarkPalette;
        var palette = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        if (SystemAccent.Resolve(theme) is { } ramp)
        {
            Set(palette, "Accent", ramp.Accent);
            Set(palette, "AccentHover", ramp.Hover);
            Set(palette, "AccentPressed", ramp.Pressed);
            Set(palette, "AccentForeground", ramp.Foreground);
        }

        return palette;
    }

    /// <summary>Overwrites both halves of a palette entry — the Color and the SolidColorBrush
    /// built from it — since the dictionary's own brushes resolved their color when it was
    /// parsed and won't pick up a replaced Color on their own.</summary>
    private static void Set(ResourceDictionary palette, string name, Color color)
    {
        palette[name + "Color"] = color;
        palette[name + "Brush"] = new SolidColorBrush(color);
    }

    private static string Describe(ResourceDictionary palette, string key)
        => palette[key] is Color c ? c.ToString() : "(none)";
}
