# Changelog

Alla nämnvärda ändringar i MyVNC dokumenteras i denna fil.

Formatet följer [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), och projektet
använder [Semantic Versioning](docs/versioning-releases.md) — se `VERSION` för den kanoniska
versionen.

## [Unreleased]

## [0.2.0-beta.11] - 2026-08-16

### Fixed
- The duplicate-connection guard only compared the exact address string used for the current
  connection attempt, so the same physical machine reached via two different addresses (e.g. LAN
  IP vs Tailscale IP for the same saved profile) could end up with two simultaneous tabs open to
  it — reproduced live: both connections dropped and reconnected in lockstep, doubling every
  reconnect/decode/render cost, driving memory up continuously and making the app sluggish enough
  to look hung. Now checks every address a profile is known by (Host IP, FQDN, Tailscale IP,
  Tailscale FQDN), not just the one being used for this specific connection attempt.

### Added
- The dashboard's Connect button (and address-picker dropdown) now disables itself, with a
  tooltip, for any profile that already has a session open — so a duplicate connection attempt is
  visibly prevented up front instead of only being caught (and silently redirected) after
  clicking. The card itself stays clickable and still switches to the existing session.

## [0.2.0-beta.10] - 2026-08-16

### Fixed
- `RfbClient` always requested the next framebuffer update as incremental, even immediately after
  a `DesktopSize` pseudo-encoding rectangle (a resolution change). `HandleDesktopResize` correctly
  reallocates the local framebuffer to the new dimensions, but an incremental request only asks
  the server for what changed from *its* point of view — it has no idea the client's buffer is now
  blank, so parts of the newly-sized framebuffer could go unpainted (server-side "nothing changed
  there" vs. client-side "I have nothing there at all"). Now requests a full (non-incremental)
  update whenever a resize rectangle was seen in the batch, which forces the server to resend
  everything. Prompted by a report of a gray/blank screen after a server-side output switch on a
  different custom RFB client, checked against MyVNC's own code as a precaution.

### Changed
- `RemoteFramebufferControl`'s root background is permanently transparent instead of opaque black.
  The remote framebuffer image only ever fills the actual desktop's aspect ratio — fit-to-window
  mode leaves letterbox bars around it, and there's no image at all before a session connects —
  and both cases were hiding the session window's decorative canvas backdrop behind solid black
  instead of letting it show through, which it's never actually done since the project's first
  commit.

## [0.2.0-beta.9] - 2026-08-15

### Fixed
- The Wake button (added in beta.8) didn't actually wake anything — confirmed via SSH that
  omarchy's "screensaver" is a normal terminal window running `ttfx`, which dismisses itself the
  instant *any single byte* arrives on its stdin (`read -n1 -t 1`). A mouse click inside a
  terminal sends nothing to the foreground process, and neither Shift nor F13 (both tried and
  confirmed sent) produce a terminal escape sequence in most emulators, so nothing ever reached
  `ttfx`. Switched the Wake button to send Space instead — guaranteed to produce a literal byte in
  every terminal. Ctrl+Alt+Del only ever "worked" because Delete happens to map to a real escape
  sequence, not because of anything DPMS/idle-related as originally assumed.
- Removed the ResourceWatchdog's MessageBox popup. Live use showed the sustained-high threshold
  trips routinely during ordinary active sessions (framebuffer decode/render load), not just
  genuine runaway incidents — a popup on every busy moment was more noise than signal. Still
  always logged to myvnc.log regardless of the debug-logging toggle, so a real runaway is still
  evidenced without interrupting the session.

## [0.2.0-beta.8] - 2026-08-15

### Added
- "Wake" button in the session toolbar, next to Ctrl+Alt+Del: sends a harmless Shift tap to wake
  a DPMS-blanked screen. Confirmed on omarchy/Hyprland that a plain VNC click can't wake a
  DPMS-off display — wlroots-based compositors stop delivering pointer events entirely while the
  screen is DPMS-off (even the lock screen's own click-to-wake handler never receives the event),
  but keyboard input isn't gated the same way, which is also why Ctrl+Alt+Del happened to work as
  an unintended side effect. Shift alone has no side effects in a normal session, unlike
  Ctrl+Alt+Del.

## [0.2.0-beta.7] - 2026-08-15

### Fixed
- Mouse wheel scrolling did nothing in a session. Same class of bug as the beta.6 click fix:
  `RemoteFramebufferControl` was wired to the bubbling `MouseWheel` event, but `ScrollHost` (the
  `ScrollViewer` wrapping the framebuffer) consumes `MouseWheel` for its own scrolling and marks
  it handled before it ever bubbles up. Switched to `PreviewMouseWheel` (tunneling), and marks the
  event handled afterward so the local view never double-scrolls in ActualSize mode. Verified live.

## [0.2.0-beta.6] - 2026-08-15

### Fixed
- **The actual root cause of "clicking a flyout does nothing", confirmed live**: `RemoteFramebufferControl` was wired to the bubbling `MouseDown`/`MouseUp` events. Debug-log evidence from a live session showed `MouseUp` reliably reaching the control on every click, but `MouseDown` never did — meaning the server was never told a button had gone down, only an already-cleared "up". Switched to the tunneling `PreviewMouseDown`/`PreviewMouseUp` events, which fire top-down before whatever was consuming the bubbling `MouseDown` (observed with a precision touchpad) gets a chance to. Verified end-to-end against a real Quickshell bar icon on <hostname> (Bluetooth flyout) — clicks now work. The beta.4 (modifier-release) and beta.5 (mouse-capture) fixes were both real, legitimate bugs, but neither was actually this one.

### Added
- "Forget SSH host key" button in the connection form: runs `ssh-keygen -R` for every filled-in
  address (Host/FQDN/Tailscale IP/Tailscale FQDN) to clear a stale known_hosts entry when a
  machine's SSH host key legitimately changes (e.g. after a reinstall). Never bypasses
  verification of whatever key shows up next — it only clears the old one.

## [0.2.0-beta.5] - 2026-08-15

### Fixed
- Captured the mouse on button-down in `RemoteFramebufferControl` and released it once every
  button is back up. Without capture, WPF only guarantees `MouseUp` routes back to the element
  that received `MouseDown` if the pointer is still over it at release time — a framebuffer
  repaint or the auto-hide topbar animating in mid-click can shift hit-testing and silently drop
  the up-event, leaving the remote compositor's view of the button stuck "down" with no matching
  release. Compared directly against TigerVNC's viewport code (which captures the pointer for its
  whole window implicitly via FLTK) — this is the concrete difference the previous beta.4 fix
  (modifier-key corruption) didn't address, and is a much better match for "clicking a flyout
  does nothing" than a compositor-side protocol limitation ever was. Also added a
  `LostMouseCapture` handler that force-sends a button-release if capture is stolen mid-click
  (e.g. a dialog popping up), mirroring the existing `ReleaseAllModifiers`-on-focus-loss fix, so a
  stuck-down button can't persist past whatever interrupted the click.

## [0.2.0-beta.4] - 2026-08-15

### Fixed
- Stopped sending unpaired modifier-release keysyms on every keyboard-focus change.
  `ReleaseAllModifiers` unconditionally sent an up-event for a fixed list of 8 modifiers
  regardless of whether any had actually been pressed, corrupting the remote compositor's
  global modifier state — evidenced by repeated "Alt_R" entries in wayvnc's own log, and traced
  (via an independent test confirming TigerVNC clicks the same flyouts fine against the same
  server) as the real cause of a symptom previously misdiagnosed as a `wlr-virtual-pointer-v1`
  protocol limitation. All key sends now route through a tracked-down-keys helper that only
  releases what's actually down.

### Docs
- Corrected README.md and the in-app Help's "known limitations" text: the click-flyout issue
  was a MyVNC bug (see above), not a Wayland/wlroots limitation as previously stated. Flagged
  as fixed-but-not-yet-re-verified end-to-end against a real Waybar/Quickshell session.

## [0.2.0-beta.3] - 2026-08-15

### Fixed
- Throttled outgoing pointer-move events to ~60Hz. `RemoteFramebufferControl.OnMouseMove` sent a
  synchronous network write+flush on every single WPF MouseMove event, completely unthrottled —
  reproduced live via ResourceWatchdog catching sustained 70-145% CPU of one core, and a raw-RFB
  probe measuring 10-16 framebuffer updates/sec continuously against the same host during the
  episode. If the remote compositor draws its cursor into the framebuffer, an unthrottled flood
  of position updates becomes a feedback loop.
- Eliminated a heap allocation on every single byte/pixel read in `ZrleDecoder` (`ReadByte`
  previously allocated a fresh `byte[1]` per call) — Plain/Palette RLE tiles on a busy screen can
  call this thousands of times per rectangle, and the allocation churn was a real, measured
  contributor to sustained CPU load on a large/actively-used session.

### Added
- Duplicate-connection guard: MainWindow.OpenSession now checks every open session window (a
  static registry, since "one window per session" mode can have several at once) for an existing
  tab already connected to the target host:port before opening a new one — focuses the existing
  tab/window instead of opening a second connection. Two simultaneous clients against the same
  wayvnc server were directly implicated in the resource-contention incident above.

## [0.2.0-beta.2] - 2026-08-15

### Added
- Opt-in debug logging (Settings toggle, off by default) writing protocol/app diagnostics to
  `%APPDATA%\MyVNC\myvnc.log` — connection lifecycle, security/encoding negotiation, disconnects,
  reconnect attempts, SSH-launch outcomes, and unhandled exceptions. Never logs credentials,
  keystrokes, or clipboard contents. A "Öppna logg"/"Open log" button opens it in Notepad when it
  exists.
- `ResourceWatchdog`: a dedicated background thread (independent of the WPF dispatcher, so it
  keeps sampling even if the UI thread itself were the one stuck) watches this process's own
  memory/CPU and warns + always logs (regardless of the debug-logging toggle) if it looks like
  MyVNC has gone amok — the exact failure mode a runaway named-pipe retry loop produced earlier
  in this project's history.

## [0.2.0-beta.1] - 2026-08-15

### Added
- Enforced single-instance: a second launch (desktop icon, taskbar jump-list shortcut) now
  forwards to the already-running instance instead of starting a disconnected process — fixes
  "open new sessions as tabs" being unreachable from jump-list shortcuts.
- SSH-terminal icon on connection cards: shown once a host's port 22 is confirmed reachable,
  launches PowerShell/Windows Terminal/WSL (configurable, auto-detects by default) with `ssh`
  pre-filled.
- In-app Help overlay: paste-key tip (Ctrl+Shift+V), how MyVNC works, wayvnc configuration
  guidance (address/port/auth), and documented known limitations (wlr-virtual-pointer-v1 not
  always triggering layer-shell popups like Waybar/Quickshell flyouts).
- Windows installer (Inno Setup, per-user install, no admin/UAC required) — see `installer/`.
- Unit test project (`tests/MyVNC.Rfb.Tests`) covering the RFB protocol library: every ZRLE
  subencoding, tile-grid boundaries, cross-rectangle continuous-zlib-stream decoding, VNC-Auth
  DES key derivation, big-endian wire helpers, and Unicode-to-keysym mapping.
- Process-level smoke test script (`scripts/smoke-test.ps1`) against the built Release exe:
  startup, single-instance enforcement, resource-growth, and clean-shutdown checks.

### Fixed
- Runaway memory growth (multi-GB in seconds) caused by the single-instance named-pipe listener
  retrying with no backoff after a failure; also hardened the instance-check itself to use a
  `Global\` mutex so a differently-elevated/sessioned launch can no longer race into believing
  it's the first instance.
- Session window's tab bar/toolbar silently swallowed clicks meant for the remote desktop near
  the top edge, even while "hidden," due to a rendered-height mismatch against a fixed
  translate-offset; now explicitly disables hit-testing while hidden.
- Unified the tab bar and single-session toolbar to the same Right-Ctrl-held reveal gesture in
  both windowed and fullscreen mode, so the remote desktop's own top bar/panel is never covered,
  resized, or offset by MyVNC's own chrome.

## [0.1.0] - 2026-08-15

### Added
- Egen RFB/VNC-klientimplementation (handskakning 3.3/3.7/3.8, None/VNC-Auth/VeNCrypt,
  Raw/CopyRect/DesktopSize/ZRLE-kodning) skräddarsydd för wayvnc/Hyprland-hosts.
- Modern WPF-dashboard med sparade anslutningar, favoritmarkering, sök/filter och stöd för
  fyra adresser per host (Host-IP, FQDN, Tailscale-IP, Tailscale-FQDN) med adressväljare.
- Flersessionsstöd: egna fönster eller flikar i samma fönster (valbart), med tydlig
  flikindikering och helskärmsbeteende som matchar enkelsessionsläget.
- Anslutningsalternativ per host: visa-endast-läge, oberoende urklippsriktningar
  (ta emot/skicka), verklig storlek vs. anpassa-till-fönster, testa anslutning-knapp.
- Automatisk återanslutning med exponentiell backoff vid oväntat tapp.
- Fullständig lokalisering (svenska, engelska, norska, danska, finska, isländska); appen
  följer OS:ets ljusa/mörka tema automatiskt.
- Windows-integration: skrivbordsikon, taskbar-genvägar (jump list) med pinnade/senaste
  anslutningar, egen app-logga som ikon.
