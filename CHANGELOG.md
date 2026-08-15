# Changelog

Alla nämnvärda ändringar i MyVNC dokumenteras i denna fil.

Formatet följer [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), och projektet
använder [Semantic Versioning](docs/versioning-releases.md) — se `VERSION` för den kanoniska
versionen.

## [Unreleased]

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
