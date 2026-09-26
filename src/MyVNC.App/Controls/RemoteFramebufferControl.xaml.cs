using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyVNC.App.Input;
using MyVNC.App.Services;
using MyVNC.Rfb;

namespace MyVNC.App.Controls;

public sealed class RemoteDisconnectedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}

public partial class RemoteFramebufferControl : UserControl
{
    private RfbClient? _client;
    private WriteableBitmap? _bitmap;

    // Incremented before an old transport is torn down.  Render/connection notifications are
    // posted from the RFB receive thread, so some can already be waiting on the WPF dispatcher
    // when a tab is closed.  They must not repaint (or mark disconnected) a subsequently opened
    // session; more importantly they then become tiny no-ops instead of keeping the UI busy
    // painting the old desktop after a quick close-and-reopen.
    private int _connectionGeneration;

    // Serializes ConnectAsync/DisconnectAsync against each other — without it, closing a tab (or a
    // fresh reconnect) while a previous ConnectAsync is still mid-flight (TCP connect/handshake)
    // let both sides read/write the _client field concurrently: DisconnectAsync could null it out
    // right as ConnectAsync's own await resumed and went to use it, corrupting state or leaking a
    // connection outright. Every entry into either method now runs strictly one-at-a-time.
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private bool _rightAltDown;
    private bool _leftCtrlSuppressed;
    private bool _leftCtrlPending;
    private bool _leftCtrlTracked; // true while we're mid-chord for LeftCtrl; guards against a duplicate KeyUp sending an unpaired Control_L-up
    private DispatcherTimer? _ctrlHoldTimer;
    private string? _lastSyncedClipboardText;

    // Every keysym we've told the remote is currently held down. ReleaseAllModifiers (fired on
    // every LostKeyboardFocus, which can happen often) used to unconditionally send an up-event
    // for a fixed list of 8 modifiers regardless of whether any of them had actually been
    // pressed — spamming the remote compositor's own logs (reported: repeated "Alt_R" entries)
    // with an up-event for a key it never saw go down. Now it only releases what's tracked here.
    private readonly HashSet<uint> _keysDown = [];

    // Deliberately no per-key logging here, unlike pointer events: this is the single choke point
    // for every keystroke, including normal typing — logging the keysym here would be a de facto
    // keylogger in myvnc.log, which conflicts with this app's own stated rule of never logging
    // keystrokes/credentials/clipboard content. The Wake/Ctrl+Alt+Del actions log at their own
    // call sites instead, since those are synthetic, non-typed, low-frequency, and worth tracing.
    private void SendKey(uint keysym, bool down)
    {
        if (down) _keysDown.Add(keysym);
        else _keysDown.Remove(keysym);
        _client?.SendKeyEvent(keysym, down);
    }

    public bool IsConnected { get; private set; }
    public string DesktopName => _client?.DesktopName ?? string.Empty;
    public int RemoteWidth => _client?.Width ?? 0;
    public int RemoteHeight => _client?.Height ?? 0;

    /// <summary>When true, the remote's framebuffer/clipboard still flow in as normal, but no
    /// keyboard, mouse, or clipboard-to-remote traffic is sent — a pure "watch only" session.</summary>
    public bool ViewOnly { get; set; }

    /// <summary>Independent clipboard-direction switches (both default on).</summary>
    public bool ReceiveClipboard { get; set; } = true;
    public bool SendClipboardEnabled { get; set; } = true;

    private bool _actualSize;
    /// <summary>False (default): fit the remote desktop to the window. True: render at native
    /// 1:1 pixel size, scrolling if it's larger than the window.</summary>
    public bool ActualSize
    {
        get => _actualSize;
        set
        {
            _actualSize = value;
            FramebufferImage.Stretch = value ? Stretch.None : Stretch.Uniform;
            FramebufferImage.HorizontalAlignment = value ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            FramebufferImage.VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            var visibility = value ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            ScrollHost.HorizontalScrollBarVisibility = visibility;
            ScrollHost.VerticalScrollBarVisibility = visibility;
        }
    }

    public event EventHandler<RemoteDisconnectedEventArgs>? Disconnected;
    public event EventHandler? Connected;

    public RemoteFramebufferControl()
    {
        InitializeComponent();

        MouseMove += OnMouseMove;
        // Tunneling (Preview*), not bubbling MouseDown/MouseUp: logging showed MouseUp reliably
        // reaching this control but MouseDown never did, for every single click — a known WPF
        // quirk where the initial contact of a click gets consumed upstream (e.g. by the
        // ScrollViewer/manipulation machinery on a precision touchpad) before the bubbling
        // MouseDown ever fires, while the corresponding MouseUp routes normally. Preview events
        // fire top-down before that can happen.
        PreviewMouseDown += OnMouseButton;
        PreviewMouseUp += OnMouseButton;
        // Same reasoning as the Preview mouse-button switch above: ScrollHost (the ScrollViewer
        // wrapping the framebuffer) consumes MouseWheel for its own scrolling and marks it
        // Handled before the bubbling MouseWheel event ever reaches this control.
        PreviewMouseWheel += OnMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        TextInput += OnTextInput;
        LostKeyboardFocus += (_, _) => ReleaseAllModifiers();
        LostMouseCapture += (_, _) => ReleaseAllButtons();
    }

    // Mirrors ReleaseAllModifiers: if capture is lost mid-click (another window/dialog steals
    // it), force-release whatever buttons we still think are down so the remote compositor never
    // sees a button stuck "down" from a lost up-event.
    private void ReleaseAllButtons()
    {
        if (_client is null || !IsConnected || _lastButtonMask == 0) return;
        var pos = _lastButtonMask;
        _lastButtonMask = 0;
        AppLog.Write($"Mouse capture lost with buttons still down (mask={pos}) — forcing release");
        var mapping = GetMapping();
        if (mapping is null) return;
        var (scale, offX, offY) = mapping.Value;
        var mousePos = Mouse.GetPosition(FramebufferImage);
        int rx = (int)((mousePos.X - offX) / scale);
        int ry = (int)((mousePos.Y - offY) / scale);
        _client.SendPointerEvent(rx, ry, 0);
    }

    public async Task ConnectAsync(RfbConnectionOptions options, CancellationToken ct = default)
    {
        await _connectionLock.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            ViewOnly = options.ViewOnly;
            ReceiveClipboard = options.ReceiveClipboard;
            SendClipboardEnabled = options.SendClipboard;
            ActualSize = options.ActualSize;

            // On a reconnect, this control's _client field just gets overwritten with a fresh
            // RfbClient below — the previous one (whose receive loop has already exited, having
            // fired ConnectionLost to get us here) was never disposed, leaving its TCP socket open.
            // Over repeated auto-reconnects that leaks a stale half-open connection per attempt,
            // which is exactly the shape of the still-unexplained duplicate-connection/memory
            // incident (see diag commit 8a9dd82) — and a server that sees multiple lingering
            // connections from the same client can plausibly serve a confused/blank framebuffer to
            // the newest one. Dispose defensively before replacing it.
            Interlocked.Increment(ref _connectionGeneration);
            if (_client is not null)
                await _client.DisposeAsync().ConfigureAwait(true);

            var client = new RfbClient();
            var generation = _connectionGeneration;
            client.FramebufferUpdated += (sender, e) => OnFramebufferUpdated(client, generation, e);
            client.DesktopResized += (sender, e) => OnDesktopResized(client, generation, e);
            client.ServerCutTextReceived += (_, e) => Dispatcher.BeginInvoke(() =>
            {
                if (!ReceiveClipboard) return;
                _lastSyncedClipboardText = e.Text;
                TrySetClipboard(e.Text);
            });
            client.ConnectionLost += (_, ex) => Dispatcher.BeginInvoke(() => RaiseDisconnected(client, generation, ex));
            _client = client;

            await client.ConnectAsync(options, ct).ConfigureAwait(true);

            _bitmap = new WriteableBitmap(client.Width, client.Height, 96, 96, PixelFormats.Bgr32, null);
            FramebufferImage.Source = _bitmap;

            // Only start receiving framebuffer data now that the bitmap exists — otherwise the
            // first full-screen update can arrive and be dropped before there's anywhere to draw it.
            client.BeginReceiving();

            IsConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync().ConfigureAwait(true);
        try
        {
            Interlocked.Increment(ref _connectionGeneration);
            IsConnected = false;
            if (_client is not null)
            {
                await _client.DisposeAsync().ConfigureAwait(true);
                _client = null;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void RaiseDisconnected(RfbClient client, int generation, Exception? ex)
    {
        if (generation != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(client, _client)) return;
        IsConnected = false;
        Disconnected?.Invoke(this, new RemoteDisconnectedEventArgs(ex));
    }

    private void OnDesktopResized(RfbClient client, int generation, DesktopResizedEventArgs e)
    {
        // Same priority as OnFramebufferUpdated so this stays ordered relative to the
        // rectangles around it instead of jumping the (lower-priority) Render queue.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (generation != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(client, _client)) return;
            _bitmap = new WriteableBitmap(e.Width, e.Height, 96, 96, PixelFormats.Bgr32, null);
            FramebufferImage.Source = _bitmap;
        });
    }

    private void OnFramebufferUpdated(RfbClient client, int generation, FramebufferUpdateEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (generation != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(client, _client)) return;
            var bmp = _bitmap;
            if (bmp is null || e.Rectangle.Width == 0 || e.Rectangle.Height == 0) return;
            var rect = new Int32Rect(e.Rectangle.X, e.Rectangle.Y, e.Rectangle.Width, e.Rectangle.Height);
            try
            {
                bmp.WritePixels(rect, e.Pixels, e.Rectangle.Width * 4, 0);
            }
            catch (ArgumentException ex)
            {
                // A resize raced with an in-flight rectangle for the old framebuffer size — safe to
                // drop, but logged (rather than silently swallowed) since a burst of these would
                // point at exactly the kind of stale-framebuffer race that produces a gray screen.
                AppLog.Write($"Dropped stale framebuffer rect {rect} against {bmp.PixelWidth}x{bmp.PixelHeight} bitmap: {ex.Message}");
            }
        });
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard can be transiently locked by another app */ }
    }

    // ----- Pointer input -----

    private (double scale, double offsetX, double offsetY)? GetMapping()
    {
        if (_bitmap is null) return null;
        if (ActualSize) return (1.0, 0.0, 0.0); // GetPosition(FramebufferImage) is already scroll-independent
        if (FramebufferImage.ActualWidth <= 0 || FramebufferImage.ActualHeight <= 0) return null;
        double scale = Math.Min(FramebufferImage.ActualWidth / _bitmap.PixelWidth, FramebufferImage.ActualHeight / _bitmap.PixelHeight);
        double renderedW = _bitmap.PixelWidth * scale;
        double renderedH = _bitmap.PixelHeight * scale;
        double offsetX = (FramebufferImage.ActualWidth - renderedW) / 2;
        double offsetY = (FramebufferImage.ActualHeight - renderedH) / 2;
        return (scale, offsetX, offsetY);
    }

    private int _lastButtonMask;

    // WPF can raise MouseMove far faster than any VNC session needs (a high-poll-rate mouse can
    // exceed 100Hz), and each send is a synchronous network write+flush — over a TLS-wrapped
    // VeNCrypt connection that's real per-call overhead. Worse, if the remote compositor draws
    // its cursor into the framebuffer, an unthrottled flood of position updates becomes a
    // feedback loop: every move we send triggers a redraw the server sends back, which we then
    // have to decode and render. A real, reproduced incident (sustained 70-145% CPU of one core)
    // traced back to exactly this. ~60Hz is smooth for a remote desktop and matches what other
    // VNC clients throttle to; button/wheel events are never throttled since they're discrete
    // and low-frequency.
    private static readonly TimeSpan PointerMoveThrottle = TimeSpan.FromMilliseconds(16);
    private DateTime _lastPointerMoveSentAt = DateTime.MinValue;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_client is null || !IsConnected || ViewOnly) return;
        var now = DateTime.UtcNow;
        if (now - _lastPointerMoveSentAt < PointerMoveThrottle) return;
        _lastPointerMoveSentAt = now;

        var mapping = GetMapping();
        if (mapping is null) return;
        var (scale, offX, offY) = mapping.Value;
        var pos = e.GetPosition(FramebufferImage);
        int rx = (int)((pos.X - offX) / scale);
        int ry = (int)((pos.Y - offY) / scale);
        _client.SendPointerEvent(rx, ry, _lastButtonMask);
    }

    // Wired to PreviewMouseDown/PreviewMouseUp (tunneling), not the bubbling MouseDown/MouseUp —
    // logging during live debugging showed MouseUp reliably reaching this control but MouseDown
    // never did, for every single click, which meant the server never learned a button had gone
    // down at all (each click sent only a spurious "up" with the mask already cleared). This is a
    // known WPF quirk: something upstream (observed with a precision-touchpad tap) can consume
    // the initial contact before a bubbling MouseDown ever fires, while the matching MouseUp still
    // routes normally. Preview events fire top-down before that has a chance to happen. This was
    // the actual root cause of "clicking a flyout does nothing" — the modifier-release and
    // mouse-capture fixes in earlier betas were both real bugs, but neither was this one.
    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        if (_client is null || !IsConnected) return;
        Focus();
        if (ViewOnly) return;

        int bit = e.ChangedButton switch
        {
            MouseButton.Left => 1 << 0,
            MouseButton.Middle => 1 << 1,
            MouseButton.Right => 1 << 2,
            _ => 0,
        };
        if (bit == 0) return;

        if (e.ButtonState == MouseButtonState.Pressed) _lastButtonMask |= bit;
        else _lastButtonMask &= ~bit;

        // Cheap extra safety net on top of the Preview-event fix above: without capture, WPF only
        // guarantees MouseUp routes back to this element if the pointer is still over it at
        // release time.
        if (_lastButtonMask != 0) CaptureMouse();
        else if (IsMouseCaptured) ReleaseMouseCapture();

        var mapping = GetMapping();
        if (mapping is null) return;
        var (scale, offX, offY) = mapping.Value;
        var pos = e.GetPosition(FramebufferImage);
        int rx = (int)((pos.X - offX) / scale);
        int ry = (int)((pos.Y - offY) / scale);
        AppLog.Write($"Pointer {(e.ButtonState == MouseButtonState.Pressed ? "down" : "up")} bit={bit} mapped=({rx},{ry})");
        _client.SendPointerEvent(rx, ry, _lastButtonMask);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_client is null || !IsConnected || ViewOnly) return;
        var mapping = GetMapping();
        if (mapping is null) return;
        var (scale, offX, offY) = mapping.Value;
        var pos = e.GetPosition(FramebufferImage);
        int rx = (int)((pos.X - offX) / scale);
        int ry = (int)((pos.Y - offY) / scale);

        int bit = e.Delta > 0 ? 1 << 3 : 1 << 4;
        _client.SendPointerEvent(rx, ry, _lastButtonMask | bit);
        _client.SendPointerEvent(rx, ry, _lastButtonMask);

        // Now that we're on PreviewMouseWheel (tunneling), the event would otherwise still reach
        // ScrollHost afterward and scroll the local view too (visible when ActualSize is on) —
        // the wheel should only ever act on the remote desktop, never locally.
        e.Handled = true;
    }

    // ----- Keyboard input -----
    // AltGr on Windows is synthesized as LeftCtrl-down immediately followed by RightAlt-down.
    // We briefly hold the LeftCtrl-down before forwarding it, so a following RightAlt can
    // cancel it and be sent as a single ISO_Level3_Shift (AltGr) keysym instead — otherwise
    // every AltGr'd character (e.g. Swedish @, {, }) would arrive at the remote as Ctrl+Alt+key.

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_client is null || !IsConnected || ViewOnly) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.LeftCtrl)
        {
            _leftCtrlTracked = true;
            _leftCtrlPending = true;
            _leftCtrlSuppressed = false;
            _ctrlHoldTimer?.Stop();
            _ctrlHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            _ctrlHoldTimer.Tick += (_, _) =>
            {
                _ctrlHoldTimer!.Stop();
                if (_leftCtrlPending && !_leftCtrlSuppressed)
                    SendKey(X11Keysyms.Control_L, true);
                _leftCtrlPending = false;
            };
            _ctrlHoldTimer.Start();
            e.Handled = true;
            return;
        }

        if (key == Key.RightAlt)
        {
            _rightAltDown = true;
            if (_leftCtrlPending)
            {
                _ctrlHoldTimer?.Stop();
                _leftCtrlSuppressed = true;
                _leftCtrlPending = false;
            }
            SendKey(X11Keysyms.AltGr, true);
            e.Handled = true;
            return;
        }

        // A LeftCtrl-down we were holding back for AltGr detection turned out to be a real
        // Ctrl-chord (this key proves it) — flush it now, before the chord's own key event,
        // so the remote sees Ctrl down first.
        if (_leftCtrlPending)
        {
            _ctrlHoldTimer?.Stop();
            if (!_leftCtrlSuppressed)
                SendKey(X11Keysyms.Control_L, true);
            _leftCtrlPending = false;
        }

        if (KeyTranslator.TryGetKeysym(key, out var keysym))
        {
            SendKey(keysym, true);
            e.Handled = true;
            return;
        }

        // Windows never raises TextInput for Ctrl/Alt-chords (Ctrl+V produces a control
        // character, not 'v'), so letters/digits used in shortcuts need explicit key events.
        if (!_rightAltDown && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0
            && KeyTranslator.TryGetBaseAsciiKeysym(key, out var asciiKeysym))
        {
            SendKey(asciiKeysym, true);
            e.Handled = true;
        }
        // Otherwise this is a plain character-producing key — fall through unhandled so
        // OnTextInput receives it (correctly resolved for shift/AltGr/dead keys/IME).
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_client is null || !IsConnected || ViewOnly) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.LeftCtrl)
        {
            _ctrlHoldTimer?.Stop();
            // Guards against a duplicate KeyUp (seen from some synthetic input sources) sending
            // a second, unpaired Control_L-up after we've already closed out this chord.
            if (_leftCtrlTracked)
            {
                if (_leftCtrlPending && !_leftCtrlSuppressed)
                {
                    // Tapped and released faster than the AltGr detection window — send both edges now.
                    SendKey(X11Keysyms.Control_L, true);
                    SendKey(X11Keysyms.Control_L, false);
                }
                else if (!_leftCtrlSuppressed)
                {
                    SendKey(X11Keysyms.Control_L, false);
                }
            }
            _leftCtrlTracked = false;
            _leftCtrlPending = false;
            _leftCtrlSuppressed = false;
            e.Handled = true;
            return;
        }

        if (key == Key.RightAlt)
        {
            _rightAltDown = false;
            SendKey(X11Keysyms.AltGr, false);
            e.Handled = true;
            return;
        }

        if (KeyTranslator.TryGetKeysym(key, out var keysym))
        {
            SendKey(keysym, false);
            e.Handled = true;
            return;
        }

        if (!_rightAltDown && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0
            && KeyTranslator.TryGetBaseAsciiKeysym(key, out var asciiKeysym))
        {
            SendKey(asciiKeysym, false);
            e.Handled = true;
        }
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_client is null || !IsConnected || ViewOnly || string.IsNullOrEmpty(e.Text)) return;
        foreach (var rune in e.Text.EnumerateRunes())
        {
            if (rune.Value < 0x20) continue; // stray control character, not real text (see Ctrl-chord handling above)
            var keysym = X11Keysyms.FromUnicode(rune.Value);
            SendKey(keysym, true);
            SendKey(keysym, false);
        }
        e.Handled = true;
    }

    private void ReleaseAllModifiers()
    {
        if (_client is null || !IsConnected) return;
        _ctrlHoldTimer?.Stop();
        _leftCtrlPending = false;
        _leftCtrlSuppressed = false;
        _leftCtrlTracked = false;
        if (_rightAltDown)
        {
            SendKey(X11Keysyms.AltGr, false);
            _rightAltDown = false;
        }
        // Release only what we've actually told the remote is down — not a fixed list of 8
        // modifiers regardless of whether any were ever pressed (see _keysDown's own comment).
        foreach (var k in _keysDown.ToArray())
            SendKey(k, false);
    }

    /// <summary>Forwards a Win-key transition captured by the system-wide keyboard hook
    /// (see <see cref="Interop.GlobalKeyboardHook"/>) as the equivalent Super keysym.</summary>
    public void SendCapturedSuperKey(bool isDown, bool isRight)
        => SendKey(isRight ? X11Keysyms.Super_R : X11Keysyms.Super_L, isDown);

    /// <summary>Sends the classic Ctrl+Alt+Del chord — needed because Windows intercepts it locally otherwise.</summary>
    public void SendCtrlAltDelete()
    {
        if (_client is null || !IsConnected) return;
        AppLog.Write("Sending Ctrl+Alt+Del");
        SendKey(X11Keysyms.Control_L, true);
        SendKey(X11Keysyms.Alt_L, true);
        SendKey(X11Keysyms.Delete, true);
        SendKey(X11Keysyms.Delete, false);
        SendKey(X11Keysyms.Alt_L, false);
        SendKey(X11Keysyms.Control_L, false);
    }

    /// <summary>Sends a harmless Space tap to dismiss omarchy's screensaver. Confirmed on
    /// omarchy/Hyprland via SSH: "the screensaver" is a normal terminal window
    /// (alacritty/foot/kitty/ghostty) running `ttfx`, launched by
    /// /usr/share/omarchy/bin/omarchy-screensaver, which dismisses itself via a plain
    /// `read -n1 -t 1` loop — it exits the instant *any single byte* arrives on the terminal's
    /// stdin. A mouse click inside a terminal doesn't send anything to the foreground process, so
    /// clicking never dismisses it. Neither did a plain Shift tap or F13 (both live-tested,
    /// confirmed sent via logging) — most terminal emulators emit no escape sequence at all for a
    /// bare modifier or an F13-F24 key, so nothing ever reached ttfx's stdin. Ctrl+Alt+Del "works"
    /// only because Delete maps to a real terminal escape sequence. Space is the safest choice
    /// that's guaranteed to produce a literal byte in every terminal, with no side effects worse
    /// than an extra space character if it lands somewhere unexpected.</summary>
    public void SendWakeNudge()
    {
        if (_client is null || !IsConnected) return;
        AppLog.Write("Sending wake nudge (Space)");
        SendKey(X11Keysyms.Space, true);
        SendKey(X11Keysyms.Space, false);
    }

    /// <summary>Pushes the local clipboard to the remote session, skipping it if nothing changed
    /// since the last sync in either direction (avoids needless round-trips/echo loops).</summary>
    public void SyncLocalClipboardToRemote()
    {
        if (_client is null || !IsConnected || ViewOnly || !SendClipboardEnabled) return;

        // Clipboard.GetText()/ContainsText() can block for up to ~1s per call (and longer if
        // another app holds the clipboard open) via WPF's internal OLE retry loop — and this
        // method is called synchronously from ClipboardMonitor's raw WndProc hook, on the UI
        // thread, every time the clipboard changes. A confirmed hang (process dump, UI thread
        // stack) showed the whole app frozen with the UI thread sitting in exactly this call.
        // Reading the clipboard needs an STA thread, but not necessarily *this* one — do it on a
        // dedicated short-lived STA thread so a contended clipboard never blocks the message pump.
        var thread = new Thread(() =>
        {
            string text;
            try
            {
                if (!Clipboard.ContainsText()) return;
                text = Clipboard.GetText();
            }
            catch { return; /* clipboard can be transiently locked by another app */ }
            Dispatcher.BeginInvoke(() => PushClipboardTextToRemote(text));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void PushClipboardTextToRemote(string text)
    {
        if (_client is null || !IsConnected || ViewOnly || !SendClipboardEnabled) return;
        if (text == _lastSyncedClipboardText) return;
        _lastSyncedClipboardText = text;
        _client.SendClientCutText(text);
    }
}
