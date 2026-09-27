using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
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

    /// <summary>Every currently open session window (regardless of Settings.SessionOpenMode —
    /// "one window per session" mode can have several of these at once), so a duplicate connect
    /// to a host that's already open anywhere can be caught and redirected instead of opening a
    /// second connection to the same server.</summary>
    private static readonly List<SessionWindow> AllWindows = [];

    /// <summary>Finds an already-open tab connected to any of these candidate addresses at this
    /// port, across every open session window. Takes every address a profile is known by (Host
    /// IP, FQDN, Tailscale IP, Tailscale FQDN), not just the one being used for this connection
    /// attempt — a real incident showed two tabs open to the same physical machine simultaneously
    /// (one via LAN IP, one via Tailscale IP), which a single-address comparison can't catch, and
    /// which doubled every reconnect/decode/render cost. Two simultaneous clients against the
    /// same wayvnc server were directly implicated in the original resource-contention incident
    /// this guard exists for, so MainWindow.OpenSession redirects here instead of opening a
    /// second connection.</summary>
    public static SessionTab? FindActiveSession(IEnumerable<string> candidateAddresses, int port)
    {
        var addresses = new HashSet<string>(candidateAddresses, StringComparer.OrdinalIgnoreCase);
        var openTabs = AllWindows.SelectMany(w => w.Tabs).ToList();
        // Temporary diagnostic: a real incident showed the same address opened twice within one
        // process's lifetime with no "Duplicate connect blocked" log line, meaning this check
        // somehow didn't match an existing, still-connected tab. Logging both sides of the
        // comparison here (candidates vs. what's actually open) turns the next occurrence into
        // hard evidence instead of another guessing round.
        AppLog.Write($"FindActiveSession: port={port} candidates=[{string.Join(",", addresses)}] " +
            $"openTabs=[{string.Join(",", openTabs.Select(t => $"{t.Options.Host}:{t.Options.Port}(connected={t.Framebuffer.IsConnected})"))}] " +
            $"windows={AllWindows.Count}");
        return openTabs.FirstOrDefault(t => addresses.Contains(t.Options.Host) && t.Options.Port == port);
    }

    /// <summary>True when one of these addresses has a tab that is *connected right now* — unlike
    /// <see cref="FindActiveSession"/>, which also matches a tab that's still connecting, sitting
    /// on a failed connect, or waiting out an auto-reconnect backoff. The dashboard's per-card
    /// "already connected" state uses this one: a connect that failed must not leave the card's
    /// Connect button and address dropdown dead until the retries give up and close the tab.</summary>
    public static bool HasConnectedSession(IEnumerable<string> candidateAddresses, int port)
    {
        var addresses = new HashSet<string>(candidateAddresses, StringComparer.OrdinalIgnoreCase);
        return AllWindows.SelectMany(w => w.Tabs)
            .Any(t => addresses.Contains(t.Options.Host) && t.Options.Port == port && t.Framebuffer.IsConnected);
    }

    /// <summary>Raised whenever a tab is added, connects, drops, or closes, so the dashboard can
    /// refresh its per-card connected state. Its own Activated hook isn't enough: a connect can
    /// fail (or an auto-reconnect give up) while the dashboard already has focus, and then no
    /// activation ever follows to clear the card's "connected" state.</summary>
    public static event Action? SessionsChanged;

    private static void NotifySessionsChanged() => SessionsChanged?.Invoke();

    /// <summary>Starts a connect attempt for a tab that isn't connected, skipping whatever
    /// auto-reconnect backoff it was waiting on. Clicking Connect on a card whose session is
    /// stuck retrying (or gave up entirely, with auto-reconnect off) means "try again now", not
    /// just "show me that window" — without this the dashboard's only other option would be a
    /// second connection to the same host, which is exactly what the duplicate guard blocks.
    /// No-op while the tab is connected or already has an attempt in flight.</summary>
    public static void RetryConnect(SessionTab tab)
        => AllWindows.FirstOrDefault(w => w.Tabs.Contains(tab))?.RetryTabNow(tab);

    public static void FocusExistingSession(SessionTab tab)
    {
        var owner = AllWindows.FirstOrDefault(w => w.Tabs.Contains(tab));
        if (owner is null) return;
        owner.SelectTab(tab);
        if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.Activate();
    }

    public ObservableCollection<SessionTab> Tabs { get; } = [];

    private SessionTab? SelectedTab => Tabs.FirstOrDefault(t => t.IsSelected);

    private WindowStyle _savedStyle;
    private WindowState _savedState;
    private ResizeMode _savedResizeMode;
    private bool _isFullscreen;

    private readonly DispatcherTimer _toolbarHideTimer;
    private const double ToolbarHiddenOffset = -48;

    /// <summary>Give up and close the tab after this many failed reconnect attempts, rather than
    /// retrying forever — a host that's genuinely gone (wrong port, powered off for good, etc.)
    /// would otherwise sit retrying indefinitely with no way out except manually noticing and
    /// closing it.</summary>
    private const int MaxReconnectAttempts = 3;

    /// <summary>How long a connection must stay up before a later drop starts the reconnect
    /// backoff over at attempt 1.</summary>
    private static readonly TimeSpan StableConnectionThreshold = TimeSpan.FromSeconds(15);

    // Polled rather than driven by routed KeyDown/KeyUp events, because RemoteFramebufferControl
    // forwards most keys to the remote via PreviewKeyDown and marks them Handled — which stops
    // the paired bubbling KeyDown from ever reaching this window while the framebuffer has focus.
    // Keyboard.IsKeyDown reflects WPF's low-level input state regardless of routing, so a short
    // poll picks up Right Ctrl reliably even without any mouse movement.
    private readonly DispatcherTimer _rightCtrlPollTimer;
    private bool _rightCtrlWasDown;

    // Left Ctrl triple-press pins/unpins the top bar visible (see PollLeftCtrlTripleTap), for
    // keyboards missing a dedicated key to hold — polled for the same reason as Right Ctrl above.
    private bool _leftCtrlWasDown;
    private int _leftCtrlPressCount;
    private DateTime _lastLeftCtrlPressUtc;
    private const int TripleCtrlWindowMs = 600;
    private bool _topBarPinned;

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

        AllWindows.Add(this);

        KeyDown += SessionWindow_KeyDown;
        StateChanged += SessionWindow_StateChanged;
        Closed += (_, _) =>
        {
            // Closing the window with X bypasses CloseTabAsync. Cancel every pending retry and
            // release its transport, or an invisible tab can keep reconnecting indefinitely.
            foreach (var tab in Tabs.ToArray())
            {
                tab.ReconnectCts?.Cancel();
                tab.ConnectCts?.Cancel();
                _ = DisconnectClosedTabAsync(tab);
            }
            Tabs.Clear();
            _clipboardMonitor?.Dispose();
            _keyboardHook.Dispose();
            if (Shared == this) Shared = null;
            AllWindows.Remove(this);
            NotifySessionsChanged();
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
        _rightCtrlPollTimer.Tick += (_, _) =>
        {
            PollRightCtrl();
            PollLeftCtrlTripleTap();
        };
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
        NotifySessionsChanged();

        _ = ConnectTabAsync(tab);
    }

    private async Task ConnectTabAsync(SessionTab tab)
    {
        tab.ConnectCts?.Dispose();
        var cts = new CancellationTokenSource();
        tab.ConnectCts = cts;
        tab.IsConnecting = true;
        try
        {
            await tab.Framebuffer.ConnectAsync(tab.Options, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // The tab was closed while this (re)connect attempt was still in flight — not a real
            // disconnect, so don't feed it into the auto-reconnect machinery below.
        }
        catch (Exception ex)
        {
            HandleTabDisconnected(tab, ex);
        }
        finally
        {
            tab.IsConnecting = false;
        }
    }

    /// <summary>See <see cref="RetryConnect"/> — the instance half, run on the window owning the tab.</summary>
    private void RetryTabNow(SessionTab tab)
    {
        if (tab.IsConnecting || tab.Framebuffer.IsConnected) return;

        AppLog.Write($"Retrying {tab.Options.Host}:{tab.Options.Port} now (Connect clicked on the dashboard)");
        tab.ReconnectCts?.Cancel();
        tab.ReconnectCts = null;
        tab.ReconnectAttempt = 0; // user-initiated: start the backoff ladder over rather than giving up sooner
        tab.StatusMessage = Loc.T("Session.Connecting", tab.Options.Host, tab.Options.Port);
        tab.IsCloseVisible = false;
        tab.IsStatusVisible = true;

        _ = ConnectTabAsync(tab);
    }

    private void OnTabConnected(SessionTab tab)
    {
        if (!Tabs.Contains(tab)) return;
        if (tab.ReconnectAttempt > 0)
            AppLog.Write($"Reconnected to {tab.Options.Host}:{tab.Options.Port} after {tab.ReconnectAttempt} attempt(s)");
        // ReconnectAttempt is deliberately not reset here — see HandleTabDisconnected.
        tab.ConnectedAtUtc = DateTime.UtcNow;
        tab.ReconnectCts?.Cancel();
        tab.ReconnectCts = null;
        NotifySessionsChanged();

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
        if (error is null || !Tabs.Contains(tab)) return; // closed tab/window

        AppLog.Write($"Tab disconnected ({tab.Options.Host}:{tab.Options.Port}): {error.Message}");
        NotifySessionsChanged();

        // Only a connection that actually stayed up earns a fresh backoff ladder; one dropped right
        // after the handshake counts as another failed attempt, so MaxReconnectAttempts still applies.
        if (tab.ConnectedAtUtc is { } connectedAt && DateTime.UtcNow - connectedAt >= StableConnectionThreshold)
            tab.ReconnectAttempt = 0;
        tab.ConnectedAtUtc = null;

        // The user (or the downgrade check) refused this server's identity — retrying would just
        // hit the same refusal, and re-prompt, on every backoff step. Park the tab instead.
        if (error is RfbServerIdentityRejectedException)
        {
            tab.ReconnectCts?.Cancel();
            tab.ReconnectCts = null;
            tab.StatusMessage = Loc.T("Trust.Rejected");
            tab.IsCloseVisible = true;
            tab.IsStatusVisible = true;
            return;
        }

        if (App.Settings.AutoReconnect)
        {
            ScheduleReconnect(tab, error);
            return;
        }

        tab.StatusMessage = Loc.T("Session.Disconnected", error.Message);
        tab.IsCloseVisible = true;
        tab.IsStatusVisible = true;
    }

    private async void ScheduleReconnect(SessionTab tab, Exception error)
    {
        tab.ReconnectAttempt++;
        if (tab.ReconnectAttempt > MaxReconnectAttempts)
        {
            AppLog.Write($"Giving up on {tab.Options.Host}:{tab.Options.Port} after {MaxReconnectAttempts} failed reconnect attempts — closing tab");
            await CloseTabAsync(tab).ConfigureAwait(true);
            return;
        }

        var delaySeconds = Math.Min(30, 2 * Math.Pow(2, tab.ReconnectAttempt - 1));
        AppLog.Write($"Scheduling reconnect for {tab.Options.Host}:{tab.Options.Port} in {delaySeconds}s (attempt {tab.ReconnectAttempt})");
        tab.StatusMessage = Loc.T("Session.Reconnecting", error.Message, (int)delaySeconds, tab.ReconnectAttempt);
        // No close button while a retry is already in flight — it auto-recovers (or auto-closes
        // after MaxReconnectAttempts) on its own within a few seconds. A prominent centered
        // "Close" button here tempted users into aborting an about-to-succeed reconnect (seen on
        // <hostname>: DPMS wake nudge drops the connection, the 2s auto-retry would have recovered
        // it, but the overlay's Close button got clicked instead, closing the window entirely).
        tab.IsCloseVisible = false;
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
        tab.ConnectCts?.Cancel();
        await tab.Framebuffer.DisconnectAsync().ConfigureAwait(true);

        var wasSelected = tab.IsSelected;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        NotifySessionsChanged();

        if (Tabs.Count == 0)
        {
            Close();
            return;
        }

        if (wasSelected)
            SelectTab(Tabs[Math.Max(0, index - 1)]);

        UpdateTopBarMode();
    }

    private static async Task DisconnectClosedTabAsync(SessionTab tab)
    {
        try
        {
            await tab.Framebuffer.DisconnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Failed to release closed session ({tab.Options.Host}:{tab.Options.Port}): {ex}");
        }
    }

    /// <summary>Decides which top bar is shown, whenever the tab count changes. Both bars behave
    /// identically either way — hidden by default, revealed only while Right Ctrl is held or the
    /// bar is pinned (see PollRightCtrl / PollLeftCtrlTripleTap) — so the remote desktop's own top
    /// bar/panel is never covered, resized, or offset by MyVNC's own chrome, in windowed mode or
    /// fullscreen.</summary>
    private void UpdateTopBarMode()
    {
        var multiTab = Tabs.Count > 1;
        TabBar.Visibility = multiTab ? Visibility.Visible : Visibility.Collapsed;
        ToolbarOverlay.Visibility = multiTab ? Visibility.Collapsed : Visibility.Visible;

        _topBarPinned = false;
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
        if (_topBarPinned) return;

        var (bar, transform) = Tabs.Count > 1 ? (TabBar, TabBarTransform) : (ToolbarOverlay, ToolbarTransform);
        _toolbarHideTimer.Stop();
        if (isDown) ShowBar(bar, transform);
        else _toolbarHideTimer.Start();
    }

    // Left Ctrl triple-press pins/unpins the top bar visible, for keyboards missing a dedicated
    // key to hold (or any key at all in some KVM/remote setups) — polled for the same reason as
    // Right Ctrl above.
    private void PollLeftCtrlTripleTap()
    {
        var isDown = Keyboard.IsKeyDown(Key.LeftCtrl);
        if (isDown == _leftCtrlWasDown) return;
        _leftCtrlWasDown = isDown;
        if (!isDown) return;

        var now = DateTime.UtcNow;
        if ((now - _lastLeftCtrlPressUtc).TotalMilliseconds > TripleCtrlWindowMs)
            _leftCtrlPressCount = 0;
        _lastLeftCtrlPressUtc = now;
        _leftCtrlPressCount++;

        if (_leftCtrlPressCount < 3) return;
        _leftCtrlPressCount = 0;
        ToggleTopBarPin();
    }

    private void ToggleTopBarPin()
    {
        _topBarPinned = !_topBarPinned;
        var (bar, transform) = Tabs.Count > 1 ? (TabBar, TabBarTransform) : (ToolbarOverlay, ToolbarTransform);
        _toolbarHideTimer.Stop();
        if (_topBarPinned) ShowBar(bar, transform);
        else if (!Keyboard.IsKeyDown(Key.RightCtrl)) _toolbarHideTimer.Start();
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

    private void Wake_Click(object sender, RoutedEventArgs e) => SelectedTab?.Framebuffer.SendWakeNudge();

    // ----- Send file (SFTP, since RFB/VNC has no file-transfer capability) -----

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = Loc.T("Session.SendFile") };
        if (dialog.ShowDialog(this) != true) return;
        await UploadFilesAsync(tab, dialog.FileNames);
    }

    private void SessionContent_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void SessionContent_Drop(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SessionTab tab) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await UploadFilesAsync(tab, paths);
    }

    private async Task UploadFilesAsync(SessionTab tab, IReadOnlyList<string> localPaths)
    {
        var files = localPaths.Where(File.Exists).ToList(); // skip directories — single-file transfer only
        if (files.Count == 0) return;

        var results = new List<FileTransferResult>();
        foreach (var path in files)
            results.Add(await FileTransferService.UploadFileAsync(tab.Options.Host, tab.Options.Username, tab.Options.Password, path));

        var succeeded = results.Where(r => r.Success).ToList();
        var failed = results.Where(r => !r.Success).ToList();

        var lines = new List<string>();
        if (succeeded.Count > 0) lines.Add(Loc.T("Session.SendFileOk", succeeded.Count));
        if (failed.Count > 0) lines.Add(Loc.T("Session.SendFileFail", string.Join(", ", failed.Select(f => f.FileName))));

        MessageBox.Show(this, string.Join("\n", lines), Loc.T("Session.SendFile"), MessageBoxButton.OK,
            failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // ----- Receive file (browse + download from ~/myvnc-shared) -----

    private SessionTab? _remoteFilesTab;

    private async void ReceiveFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab) return;
        _remoteFilesTab = tab;

        RemoteFilesList.ItemsSource = null;
        RemoteFilesList.Visibility = Visibility.Collapsed;
        DownloadSelectedFilesButton.IsEnabled = false;
        RemoteFilesStatusText.Visibility = Visibility.Visible;
        RemoteFilesStatusText.Text = Loc.T("Session.ReceiveFileLoading");
        RemoteFilesOverlay.Visibility = Visibility.Visible;

        IReadOnlyList<RemoteFile> files;
        try
        {
            files = await FileTransferService.ListSharedFilesAsync(tab.Options.Host, tab.Options.Username, tab.Options.Password);
        }
        catch (Exception ex)
        {
            RemoteFilesStatusText.Text = Loc.T("Session.ReceiveFileError", ex.Message);
            return;
        }

        if (_remoteFilesTab != tab || RemoteFilesOverlay.Visibility != Visibility.Visible) return; // closed/switched while loading

        if (files.Count == 0)
        {
            RemoteFilesStatusText.Text = Loc.T("Session.ReceiveFileEmpty");
            return;
        }

        RemoteFilesStatusText.Visibility = Visibility.Collapsed;
        RemoteFilesList.Visibility = Visibility.Visible;
        RemoteFilesList.ItemsSource = files;
    }

    private void RemoteFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => DownloadSelectedFilesButton.IsEnabled = RemoteFilesList.SelectedItems.Count > 0;

    private async void DownloadSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteFilesTab is not { } tab) return;
        var selected = RemoteFilesList.SelectedItems.Cast<RemoteFile>().ToList();
        if (selected.Count == 0) return;

        var folderDialog = new OpenFolderDialog { Title = Loc.T("Session.ReceiveFileChooseFolder") };
        if (folderDialog.ShowDialog(this) != true) return;

        var results = new List<FileTransferResult>();
        foreach (var file in selected)
            results.Add(await FileTransferService.DownloadFileAsync(tab.Options.Host, tab.Options.Username, tab.Options.Password, file.Name, folderDialog.FolderName));

        RemoteFilesOverlay.Visibility = Visibility.Collapsed;

        var succeeded = results.Where(r => r.Success).ToList();
        var failed = results.Where(r => !r.Success).ToList();

        var lines = new List<string>();
        if (succeeded.Count > 0) lines.Add(Loc.T("Session.ReceiveFileOk", succeeded.Count, folderDialog.FolderName));
        if (failed.Count > 0) lines.Add(Loc.T("Session.SendFileFail", string.Join(", ", failed.Select(f => f.FileName))));

        MessageBox.Show(this, string.Join("\n", lines), Loc.T("Session.ReceiveFile"), MessageBoxButton.OK,
            failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void RemoteFilesOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => RemoteFilesOverlay.Visibility = Visibility.Collapsed;

    private void RemoteFilesCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CloseRemoteFiles_Click(object sender, RoutedEventArgs e) => RemoteFilesOverlay.Visibility = Visibility.Collapsed;

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
