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

    public async Task ConnectAsync(RfbConnectionOptions options)
    {
        ViewOnly = options.ViewOnly;
        ReceiveClipboard = options.ReceiveClipboard;
        SendClipboardEnabled = options.SendClipboard;
        ActualSize = options.ActualSize;

        _client = new RfbClient();
        _client.FramebufferUpdated += OnFramebufferUpdated;
        _client.DesktopResized += OnDesktopResized;
        _client.ServerCutTextReceived += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (!ReceiveClipboard) return;
            _lastSyncedClipboardText = e.Text;
            TrySetClipboard(e.Text);
        });
        _client.ConnectionLost += (_, ex) => Dispatcher.BeginInvoke(() => RaiseDisconnected(ex));

        await _client.ConnectAsync(options).ConfigureAwait(true);

        _bitmap = new WriteableBitmap(_client.Width, _client.Height, 96, 96, PixelFormats.Bgr32, null);
        FramebufferImage.Source = _bitmap;

        // Only start receiving framebuffer data now that the bitmap exists — otherwise the
        // first full-screen update can arrive and be dropped before there's anywhere to draw it.
        _client.BeginReceiving();

        IsConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(true);
            _client = null;
        }
    }

    private void RaiseDisconnected(Exception? ex)
    {
        IsConnected = false;
        Disconnected?.Invoke(this, new RemoteDisconnectedEventArgs(ex));
    }

    private void OnDesktopResized(object? sender, DesktopResizedEventArgs e)
    {
        // Same priority as OnFramebufferUpdated so this stays ordered relative to the
        // rectangles around it instead of jumping the (lower-priority) Render queue.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _bitmap = new WriteableBitmap(e.Width, e.Height, 96, 96, PixelFormats.Bgr32, null);
            FramebufferImage.Source = _bitmap;
        });
    }

    private void OnFramebufferUpdated(object? sender, FramebufferUpdateEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            var bmp = _bitmap;
            if (bmp is null || e.Rectangle.Width == 0 || e.Rectangle.Height == 0) return;
            var rect = new Int32Rect(e.Rectangle.X, e.Rectangle.Y, e.Rectangle.Width, e.Rectangle.Height);
            try
            {
                bmp.WritePixels(rect, e.Pixels, e.Rectangle.Width * 4, 0);
            }
            catch (ArgumentException)
            {
                // A resize raced with an in-flight rectangle for the old framebuffer size — safe to drop.
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
        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch { return; /* clipboard can be transiently locked by another app */ }

        if (text == _lastSyncedClipboardText) return;
        _lastSyncedClipboardText = text;
        _client.SendClientCutText(text);
    }
}
