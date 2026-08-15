# MyVNC

A custom-built VNC client for Windows, made to connect cleanly to [Hyprland](https://hyprland.org/)/[omarchy](https://omarchy.org/) machines running [wayvnc](https://github.com/any1/wayvnc) over Tailscale. Built from scratch (own RFB protocol implementation, no bundled VNC library).

![MyVNC dashboard](docs/assets/dashboard.png)

| Tabbed sessions |
|---|
| ![Session tab: <hostname>](docs/assets/session1.png) |
| ![Session tab: <hostname>](docs/assets/session2.png) |

> The tab bar/toolbar shown above isn't always on screen — see **"Hold Right Ctrl to reveal the top bar"** below.

## Features

- **Own RFB/VNC client** — version handshake 3.3/3.7/3.8, None/VNC-Auth/VeNCrypt security (covers wayvnc's Plain/TLSPlain/X509Plain), Raw/CopyRect/DesktopSize/ZRLE encodings.
- **Multi-session** — open new connections as separate windows or as tabs in one window, switchable in Settings.
- **Hold Right Ctrl to reveal the top bar** — MyVNC's own chrome (tab bar/toolbar) is hidden by default in both windowed and fullscreen mode. **Hold Right Ctrl and move the pointer to the top edge to reveal it** — this is required, not optional; without it the bar stays hidden so it never covers the remote desktop's own panel.
- **Four addresses per host** — Host IP, FQDN, Tailscale IP, Tailscale FQDN, with a default per host and a one-off picker on every connect.
- **Per-host connection options** — view-only mode, independent clipboard directions (receive/send), actual-size vs. fit-to-window, a test-connection reachability check.
- **Auto-reconnect** with exponential backoff after an unexpected drop.
- **SSH terminal shortcut** — a per-card icon appears once a host's SSH port is confirmed reachable, launching PowerShell/Windows Terminal/WSL with `ssh` pre-filled.
- **Opt-in debug logging** — off by default, toggle it in Settings. Writes connection lifecycle, security/encoding negotiation, disconnects, and reconnect attempts to `%APPDATA%\MyVNC\myvnc.log`, viewable via the "Open log" button — never credentials, keystrokes, or clipboard contents. A background watchdog also self-monitors memory/CPU and warns (always logged, regardless of the toggle) if the app itself looks like it's misbehaving.
- **Full localization** — Swedish, English, Norwegian, Danish, Finnish, Icelandic. Follows the OS light/dark theme automatically.
- **Windows integration** — desktop shortcut, taskbar jump-list (pinned + recent hosts, launches sessions directly), single-instance enforced so a jump-list click joins the already-running window instead of starting a disconnected process.
- **In-app Help** — paste-key gotcha (Ctrl+Shift+V, not Ctrl+V), how the client works, and wayvnc configuration pointers, all available from the dashboard's `?` button.

## Installing

**Download the installer:** [MyVNC-Setup-0.2.0-beta.2.exe](https://github.com/QforA42/MyVNC/releases/download/v0.2.0-beta.2/MyVNC-Setup-0.2.0-beta.2.exe) (or browse all [releases](https://github.com/QforA42/MyVNC/releases)) — per-user install, no admin rights or UAC prompt required.

**Build your own installer instead:**

```powershell
installer\build-installer.ps1
```

This publishes a self-contained Release build and produces `installer\output\MyVNC-Setup-<version>.exe`. See [installer/](installer/).

**From source:**

```powershell
dotnet build MyVNC.slnx -c Release
```

The exe lands in `src\MyVNC.App\bin\Release\net10.0-windows\MyVNC.App.exe`.

## Configuring the remote host

The dashboard's `?` (Help) button covers this in the app itself, but in short, on the wayvnc side you generally want:

```ini
address=0.0.0.0
port=5900
enable_auth=true
enable_pam=true
certificate_file=~/.config/wayvnc/certificate.pem
private_key_file=~/.config/wayvnc/private_key.pem
```

`enable_pam=true` means you log in with your normal Linux account credentials — exactly what MyVNC sends. Keyboard layout usually follows the system's own layout automatically; only set `xkb_layout` explicitly in wayvnc's config if å/ä/ö or similar keys don't work.

## Apparent known limitations with Hyprland as of 2026-08-15

**~~Clicking flyouts/popups in status bars sometimes did nothing~~ — corrected, this was a MyVNC bug, not a Wayland/wlroots limitation.** Earlier testing (raw RFB clicks bypassing MyVNC's UI, reproducing the same failure) pointed at `wlr-virtual-pointer-v1` itself as the culprit. That conclusion was wrong. Two real MyVNC bugs contributed:

1. `ReleaseAllModifiers` sent an up-event for a fixed list of 8 modifiers on every focus change, regardless of whether any had actually been pressed — corrupting Hyprland's global modifier state (also the source of the "Alt_R" log spam below). Fixed in `0.2.0-beta.3`.
2. `RemoteFramebufferControl` never called `CaptureMouse()` on button-down. Without capture, WPF only guarantees `MouseUp` routes back to the same element that received `MouseDown` if the pointer is still over it at release — a framebuffer repaint or the auto-hide topbar animating in mid-click can shift hit-testing and silently drop the up-event, leaving the remote compositor's view of the button stuck "down" with no matching release. Found by comparing directly against TigerVNC's viewport code, which avoids this entirely because its toolkit (FLTK) implicitly captures the pointer for the whole window. Fixed in `0.2.0-beta.5`.

**Confirmation against a real Waybar/Quickshell session is still pending** — both fixes address real, confirmed bugs, but haven't yet been re-verified end-to-end against the original click-flyout symptom.

**Repeated "Alt_R" entries in wayvnc/Hyprland's own log.** Same root cause and same fix as (1) above — `ReleaseAllModifiers` was sending an unpaired release for a modifier the compositor never saw go down, on every keyboard-focus change. Fixed in `0.2.0-beta.3`.

**A remote screen that looks "asleep" may not wake from mouse/keyboard input sent over VNC — check whether it's actually suspended, not just locked.** A host that's merely screen-locked or DPMS-blanked stays fully reachable on the network, and synthetic input over an already-open RFB connection *can* wake it. A host that's fully suspended drops off the network entirely, and no VNC client can wake a suspended kernel through input events, because there's no running network stack left to deliver them to. If a session won't wake, `ping` the host first — reachable-but-unresponsive points at a Hyprland/compositor-side issue (e.g. a stuck idle/lock screen); unreachable means check the machine's idle/suspend configuration (`hypridle`, `systemd-logind`'s `IdleAction`), not MyVNC.

## Development

See [AGENTS.md](AGENTS.md) for versioning rules, commit conventions, and the build workflow. Quick reference:

```powershell
dotnet build MyVNC.slnx                      # Debug
dotnet build MyVNC.slnx -c Release            # Release (what the desktop shortcut runs)
dotnet test tests\MyVNC.Rfb.Tests             # RFB protocol unit tests
powershell -File scripts\smoke-test.ps1       # process-level regression check
```
