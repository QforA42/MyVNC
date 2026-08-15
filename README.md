# MyVNC

A custom-built VNC client for Windows, made to connect cleanly to [Hyprland](https://hyprland.org/)/[omarchy](https://omarchy.org/) machines running [wayvnc](https://github.com/any1/wayvnc) over Tailscale. Built from scratch (own RFB protocol implementation, no bundled VNC library).

![MyVNC dashboard](docs/assets/dashboard.png)

## Features

- **Own RFB/VNC client** — version handshake 3.3/3.7/3.8, None/VNC-Auth/VeNCrypt security (covers wayvnc's Plain/TLSPlain/X509Plain), Raw/CopyRect/DesktopSize/ZRLE encodings.
- **Multi-session** — open new connections as separate windows or as tabs in one window, switchable in Settings. Fullscreen and windowed modes behave identically: MyVNC's own chrome is hidden by default, revealed only while holding Right Ctrl at the top edge, so it never covers the remote desktop's own panel.
- **Four addresses per host** — Host IP, FQDN, Tailscale IP, Tailscale FQDN, with a default per host and a one-off picker on every connect.
- **Per-host connection options** — view-only mode, independent clipboard directions (receive/send), actual-size vs. fit-to-window, a test-connection reachability check.
- **Auto-reconnect** with exponential backoff after an unexpected drop.
- **SSH terminal shortcut** — a per-card icon appears once a host's SSH port is confirmed reachable, launching PowerShell/Windows Terminal/WSL with `ssh` pre-filled.
- **Full localization** — Swedish, English, Norwegian, Danish, Finnish, Icelandic. Follows the OS light/dark theme automatically.
- **Windows integration** — desktop shortcut, taskbar jump-list (pinned + recent hosts, launches sessions directly), single-instance enforced so a jump-list click joins the already-running window instead of starting a disconnected process.
- **In-app Help** — paste-key gotcha (Ctrl+Shift+V, not Ctrl+V), how the client works, and wayvnc configuration pointers, all available from the dashboard's `?` button.

## Installing

**Installer (recommended for casual use):** build one with:

```powershell
installer\build-installer.ps1
```

This publishes a self-contained Release build and produces `installer\output\MyVNC-Setup-<version>.exe` — per-user install, no admin rights or UAC prompt required. See [installer/](installer/).

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

## Known limitations with Hyprland

**Clicking flyouts/popups in status bars (Waybar, Quickshell panels — e.g. the WiFi or battery icon) sometimes does nothing.** This was traced all the way down to the protocol level: raw RFB clicks sent directly via the library, bypassing MyVNC's UI entirely, reproduce the exact same behavior. It's a known limitation of `wlr-virtual-pointer-v1`, the Wayland protocol wayvnc uses to inject the pointer into the compositor — regular mouse movement, clicking inside windows, and keyboard input all work normally; it's specifically opening a *new* popup surface (layer-shell) via a synthetic pointer that isn't always fully supported by the compositor. There's nothing MyVNC can fix client-side for this — a fix would have to come from a newer wlroots/Hyprland/wayvnc version, or the panel's own configuration on the remote machine.

**A remote screen that looks "asleep" may not wake from mouse/keyboard input sent over VNC — check whether it's actually suspended, not just locked.** A host that's merely screen-locked or DPMS-blanked stays fully reachable on the network, and synthetic input over an already-open RFB connection *can* wake it. A host that's fully suspended drops off the network entirely, and no VNC client can wake a suspended kernel through input events, because there's no running network stack left to deliver them to. If a session won't wake, `ping` the host first — reachable-but-unresponsive points at a Hyprland/compositor-side issue (e.g. a stuck idle/lock screen); unreachable means check the machine's idle/suspend configuration (`hypridle`, `systemd-logind`'s `IdleAction`), not MyVNC.

## Development

See [AGENTS.md](AGENTS.md) for versioning rules, commit conventions, and the build workflow. Quick reference:

```powershell
dotnet build MyVNC.slnx                      # Debug
dotnet build MyVNC.slnx -c Release            # Release (what the desktop shortcut runs)
dotnet test tests\MyVNC.Rfb.Tests             # RFB protocol unit tests
powershell -File scripts\smoke-test.ps1       # process-level regression check
```
