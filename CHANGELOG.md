# Changelog

Alla nämnvärda ändringar i MyVNC dokumenteras i denna fil.

Formatet följer [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), och projektet
använder [Semantic Versioning](docs/versioning-releases.md) — se `VERSION` för den kanoniska
versionen.

## [Unreleased]

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
