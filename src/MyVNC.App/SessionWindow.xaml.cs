using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private const double ToolbarHiddenOffset = -48;

    // Polled rather than driven by routed KeyDown/KeyUp events, because RemoteFramebufferControl
    // forwards most keys to the remote via PreviewKeyDown and marks them Handled — which stops
    // the paired bubbling KeyDown from ever reaching this window while the framebuffer has focus.
    // Keyboard.IsKeyDown reflects WPF's low-level input state regardless of routing, so a short
    // poll picks up Right Ctrl reliably even without any mouse movement.
    private readonly DispatcherTimer _rightCtrlPollTimer;
    private bool _rightCtrlWasDown;

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
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            var (bar, transform) = Tabs.Count > 1 ? (TabBar, TabBarTransform) : (ToolbarOverlay, ToolbarTransform);
            HideBar(bar, transform);
        };

        _rightCtrlPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _rightCtrlPollTimer.Tick += (_, _) => PollRightCtrl();
        _rightCtrlPollTimer.Start();
        Closed += (_, _) => _rightCtrlPollTimer.Stop();

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
        if (tab.ReconnectAttempt > 0)
            AppLog.Write($"Reconnected to {tab.Options.Host}:{tab.Options.Port} after {tab.ReconnectAttempt} attempt(s)");
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
            ShowBar(ToolbarOverlay, ToolbarTransform);
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

        AppLog.Write($"Tab disconnected ({tab.Options.Host}:{tab.Options.Port}): {error.Message}");

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
        AppLog.Write($"Scheduling reconnect for {tab.Options.Host}:{tab.Options.Port} in {delaySeconds}s (attempt {tab.ReconnectAttempt})");
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

    /// <summary>Decides which top bar is shown, whenever the tab count changes. Both bars behave
    /// identically either way — hidden by default, revealed only while Right Ctrl is held (see
    /// PollRightCtrl) — so the remote desktop's own top bar/panel is never covered, resized, or
    /// offset by MyVNC's own chrome, in windowed mode or fullscreen.</summary>
    private void UpdateTopBarMode()
    {
        var multiTab = Tabs.Count > 1;
        TabBar.Visibility = multiTab ? Visibility.Visible : Visibility.Collapsed;
        ToolbarOverlay.Visibility = multiTab ? Visibility.Collapsed : Visibility.Visible;

        var (bar, transform) = multiTab ? (TabBar, TabBarTransform) : (ToolbarOverlay, ToolbarTransform);
        _toolbarHideTimer.Stop();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = ToolbarHiddenOffset;
        bar.IsHitTestVisible = false;
    }

    private void PollRightCtrl()
    {
        var isDown = Keyboard.IsKeyDown(Key.RightCtrl);
        if (isDown == _rightCtrlWasDown) return;
        _rightCtrlWasDown = isDown;

        var (bar, transform) = Tabs.Count > 1 ? (TabBar, TabBarTransform) : (ToolbarOverlay, ToolbarTransform);
        _toolbarHideTimer.Stop();
        if (isDown) ShowBar(bar, transform);
        else _toolbarHideTimer.Start();
    }

    private void ShowBar(Border bar, TranslateTransform transform)
    {
        bar.IsHitTestVisible = true;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Disables hit-testing immediately (not just once the slide-up animation finishes)
    /// so a rendered-height mismatch against the fixed ToolbarHiddenOffset can never leave a
    /// sliver of the bar still clickable — which was silently swallowing clicks aimed at the
    /// remote desktop's own top bar/panel right underneath it.</summary>
    private void HideBar(Border bar, TranslateTransform transform)
    {
        bar.IsHitTestVisible = false;
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
