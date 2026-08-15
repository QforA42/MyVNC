using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MyVNC.App.Services;
using MyVNC.Rfb;

namespace MyVNC.App;

/// <summary>
/// A window hosting one or more VNC sessions as tabs. In "one window per session" mode
/// (see Settings) every connection gets its own <see cref="SessionWindow"/> with a single,
/// permanent tab — the tab bar only appears once a second session joins the same window.
/// Owns the chrome shared across tabs: fullscreen, clipboard sync, and Windows-key capture,
/// all of which act on whichever tab is currently selected.
/// </summary>
public partial class SessionWindow : Window
{
    /// <summary>The most recently created tabbed window, so "open as tab" mode knows where to
    /// add new sessions. Null once that window is closed.</summary>
    public static SessionWindow? Shared { get; private set; }

    public ObservableCollection<SessionTab> Tabs { get; } = [];

    private SessionTab? SelectedTab => Tabs.FirstOrDefault(t => t.IsSelected);

    private WindowStyle _savedStyle;
    private WindowState _savedState;
    private ResizeMode _savedResizeMode;
    private bool _isFullscreen;

    private readonly DispatcherTimer _toolbarHideTimer;
    private bool _toolbarVisible;
    private const double ToolbarHiddenOffset = -48;

    // Generous since the reveal is gated behind holding Right Ctrl (see RootGrid_MouseMove) —
    // plain mouse movement near the top can't steal clicks from the remote desktop's own top
    // bar/panel (e.g. Waybar on Hyprland).
    private const double ToolbarHotZone = 48;

    private Interop.ClipboardMonitor? _clipboardMonitor;
    private readonly Interop.GlobalKeyboardHook _keyboardHook = new();

    public SessionWindow(RfbConnectionOptions options)
    {
        InitializeComponent();
        DataContext = this;
        Interop.DarkTitleBar.Apply(this);
        Icon = Interop.AppIconFactory.Create();

        FullscreenButton.Content = Loc.T("Session.Fullscreen");
        DisconnectButton.Content = Loc.T("Session.Disconnect");
        TabFullscreenButton.Content = Loc.T("Session.Fullscreen");
        TabDisconnectButton.Content = Loc.T("Session.Disconnect");
        UpdateActualSizeButtons();

        KeyDown += SessionWindow_KeyDown;
        StateChanged += SessionWindow_StateChanged;
        Closed += (_, _) =>
        {
            _clipboardMonitor?.Dispose();
            _keyboardHook.Dispose();
            if (Shared == this) Shared = null;
        };

        SourceInitialized += (_, _) =>
        {
            _clipboardMonitor = new Interop.ClipboardMonitor(this);
            _clipboardMonitor.ClipboardChanged += () => SelectedTab?.Framebuffer.SyncLocalClipboardToRemote();
        };

        // Capture the Windows key system-wide only while this window is active and the active
        // tab is connected — otherwise Win+. (emoji picker), Win+Tab, Win+D etc. are swallowed
        // by Windows Shell before any app-level handler ever sees them, so Hyprland's
        // Super-based keybinds could never reach the remote.
        _keyboardHook.ShouldCapture = () => IsActive && SelectedTab?.Framebuffer.IsConnected == true;
        _keyboardHook.SuperKeyCaptured += (isDown, isRight) => Dispatcher.BeginInvoke(() => SelectedTab?.Framebuffer.SendCapturedSuperKey(isDown, isRight));
        _keyboardHook.Install();

        _toolbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _toolbarHideTimer.Tick += (_, _) => { _toolbarHideTimer.Stop(); HideBar(Tabs.Count > 1 ? TabBarTransform : ToolbarTransform); };

        Shared = this;
        AddTab(options);
    }

    /// <summary>Adds a new session as a tab in this window (used both for the window's first
    /// tab and by "open as tab" mode when a session is added to an already-open window).</summary>
    public void AddTab(RfbConnectionOptions options)
    {
        var tab = new SessionTab(options);
        tab.Framebuffer.Connected += (_, _) => Dispatcher.Invoke(() => OnTabConnected(tab));
        tab.Framebuffer.Disconnected += (_, e) => Dispatcher.Invoke(() => OnTabDisconnected(tab, e.Error));
        tab.Framebuffer.GotKeyboardFocus += (_, _) => tab.Framebuffer.SyncLocalClipboardToRemote();
        tab.StatusMessage = Loc.T("Session.Connecting", options.Host, options.Port);

        Tabs.Add(tab);
        SelectTab(tab);
        UpdateTopBarMode();

        _ = ConnectTabAsync(tab);
    }

    private async Task ConnectTabAsync(SessionTab tab)
    {
        try
        {
            await tab.Framebuffer.ConnectAsync(tab.Options).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HandleTabDisconnected(tab, ex);
        }
    }

    private void OnTabConnected(SessionTab tab)
    {
        tab.ReconnectAttempt = 0;
        tab.ReconnectCts?.Cancel();
        tab.ReconnectCts = null;

        tab.IsStatusVisible = false;
        if (tab == SelectedTab)
        {
            tab.Framebuffer.Focus();
            UpdateSingleSessionHeader(tab);
        }

        if (Tabs.Count <= 1)
        {
            // Briefly reveal the toolbar so the user notices it, then let it auto-hide.
            ShowBar(ToolbarTransform);
            _toolbarHideTimer.Stop();
            _toolbarHideTimer.Start();
        }
    }

    private void OnTabDisconnected(SessionTab tab, Exception? error) => HandleTabDisconnected(tab, error);

    /// <summary>Shared by both an unexpected mid-session drop (Framebuffer.Disconnected) and a
    /// failed (re)connect attempt (ConnectTabAsync's catch) — either way, when auto-reconnect is
    /// on, keep retrying with backoff until it succeeds or the user closes the tab.</summary>
    private void HandleTabDisconnected(SessionTab tab, Exception? error)
    {
        if (error is null) return; // clean disconnect via CloseTabAsync, tab is going away anyway

        if (App.Settings.AutoReconnect)
        {
            ScheduleReconnect(tab, error);
            return;
        }

        tab.StatusMessage = Loc.T("Session.Disconnected", error.Message);
        tab.IsCloseVisible = true;
        tab.IsStatusVisible = true;
    }

    private void ScheduleReconnect(SessionTab tab, Exception error)
    {
        tab.ReconnectAttempt++;
        var delaySeconds = Math.Min(30, 2 * Math.Pow(2, tab.ReconnectAttempt - 1));
        tab.StatusMessage = Loc.T("Session.Reconnecting", error.Message, (int)delaySeconds, tab.ReconnectAttempt);
        tab.IsCloseVisible = true;
        tab.IsStatusVisible = true;

        tab.ReconnectCts?.Cancel();
        var cts = new CancellationTokenSource();
        tab.ReconnectCts = cts;
        _ = ReconnectAfterDelayAsync(tab, TimeSpan.FromSeconds(delaySeconds), cts.Token);
    }

    private async Task ReconnectAfterDelayAsync(SessionTab tab, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested || !Tabs.Contains(tab)) return;

        tab.StatusMessage = Loc.T("Session.Connecting", tab.Options.Host, tab.Options.Port);
        await ConnectTabAsync(tab).ConfigureAwait(true);
    }

    private void UpdateSingleSessionHeader(SessionTab tab)
    {
        SessionTitleText.Text = string.IsNullOrWhiteSpace(tab.Framebuffer.DesktopName) ? Loc.T("Session.ConnectedFallbackName") : tab.Framebuffer.DesktopName;
        SessionSubtitleText.Text = $"{tab.Framebuffer.RemoteWidth}×{tab.Framebuffer.RemoteHeight} · {Loc.T("Session.ToolbarHint")}";
    }

    private void SelectTab(SessionTab tab)
    {
        foreach (var t in Tabs) t.IsSelected = ReferenceEquals(t, tab);
        if (!tab.IsStatusVisible) UpdateSingleSessionHeader(tab);
        UpdateActualSizeButtons();

        // Deferred: the DataTrigger that flips this tab's container from Collapsed to Visible
        // hasn't necessarily finished its layout pass yet, and WPF silently drops Focus() calls
        // on elements that aren't (yet) part of a visible visual tree — so keyboard input could
        // keep going to whichever tab had focus before, making the new tab look unresponsive.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => tab.Framebuffer.Focus());
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SessionTab tab) SelectTab(tab);
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SessionTab tab) await CloseTabAsync(tab).ConfigureAwait(true);
    }

    private async void StatusCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SessionTab tab) await CloseTabAsync(tab).ConfigureAwait(true);
    }

    private async Task CloseTabAsync(SessionTab tab)
    {
        tab.ReconnectCts?.Cancel();
        await tab.Framebuffer.DisconnectAsync().ConfigureAwait(true);

        var wasSelected = tab.IsSelected;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            Close();
            return;
        }

        if (wasSelected)
            SelectTab(Tabs[Math.Max(0, index - 1)]);

        UpdateTopBarMode();
    }

    /// <summary>Decides which top bar is shown and how, whenever the tab count or fullscreen
    /// state changes. Single tab: the auto-hide toolbar (unchanged, always gesture-gated).
    /// Multiple tabs, windowed: the tab bar is pinned — it's the only way to switch/close
    /// sessions. Multiple tabs, fullscreen: the tab bar switches to the exact same
    /// Right-Ctrl-plus-top-edge auto-hide behavior, so it doesn't permanently cover the remote
    /// desktop's own top bar/panel.</summary>
    private void UpdateTopBarMode()
    {
        var multiTab = Tabs.Count > 1;
        TabBar.Visibility = multiTab ? Visibility.Visible : Visibility.Collapsed;
        ToolbarOverlay.Visibility = multiTab ? Visibility.Collapsed : Visibility.Visible;

        if (!multiTab) return; // single-session toolbar manages its own show/hide elsewhere

        _toolbarHideTimer.Stop();
        TabBarTransform.BeginAnimation(TranslateTransform.YProperty, null);
        _toolbarVisible = !_isFullscreen;
        TabBarTransform.Y = _isFullscreen ? ToolbarHiddenOffset : 0;
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        var multiTab = Tabs.Count > 1;
        if (multiTab && !_isFullscreen) return; // tab bar is pinned; no gesture needed

        var transform = multiTab ? TabBarTransform : ToolbarTransform;
        var y = e.GetPosition(RootGrid).Y;
        // Gated behind Right Ctrl so plain mouse movement near the top never steals clicks
        // from the remote desktop's own top bar/panel (e.g. Waybar on Hyprland) — only a
        // deliberate "hold Right Ctrl + go to the top edge" reveals the bar.
        if (Keyboard.IsKeyDown(Key.RightCtrl) && y <= ToolbarHotZone)
        {
            _toolbarHideTimer.Stop();
            ShowBar(transform);
        }
        else if (_toolbarVisible)
        {
            _toolbarHideTimer.Stop();
            _toolbarHideTimer.Start();
        }
    }

    private void ShowBar(TranslateTransform transform)
    {
        _toolbarVisible = true;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void HideBar(TranslateTransform transform)
    {
        _toolbarVisible = false;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(ToolbarHiddenOffset, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is { } tab) await CloseTabAsync(tab).ConfigureAwait(true);
    }

    private void CtrlAltDel_Click(object sender, RoutedEventArgs e) => SelectedTab?.Framebuffer.SendCtrlAltDelete();

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab) return;
        tab.Framebuffer.ActualSize = !tab.Framebuffer.ActualSize;
        UpdateActualSizeButtons();
    }

    private void UpdateActualSizeButtons()
    {
        var label = Loc.T(SelectedTab?.Framebuffer.ActualSize == true ? "Session.FitWindow" : "Session.ActualSize");
        ActualSizeButton.Content = label;
        TabActualSizeButton.Content = label;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void SessionWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void SessionWindow_StateChanged(object? sender, EventArgs e)
    {
        // Native maximize (double-click title bar / the OS maximize button) should also
        // drop the window chrome, so "maximized" and "fullscreen" feel like the same thing.
        if (WindowState == WindowState.Maximized && !_isFullscreen)
            EnterFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        _savedStyle = WindowStyle;
        _savedState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState;
        _savedResizeMode = ResizeMode;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
        FullscreenButton.Content = Loc.T("Session.ExitFullscreen");
        TabFullscreenButton.Content = Loc.T("Session.ExitFullscreen");
        UpdateTopBarMode();
    }

    private void ExitFullscreen()
    {
        WindowStyle = _savedStyle;
        ResizeMode = _savedResizeMode;
        WindowState = _savedState;
        _isFullscreen = false;
        FullscreenButton.Content = Loc.T("Session.Fullscreen");
        TabFullscreenButton.Content = Loc.T("Session.Fullscreen");
        UpdateTopBarMode();
    }
}
