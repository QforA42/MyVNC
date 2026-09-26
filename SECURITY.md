# Security Policy

MyVNC is a remote-desktop client: it handles credentials, sends your keyboard and mouse input to
another machine and shows that machine's screen. Security issues are taken seriously, and
responsible disclosure is appreciated.

## Supported versions

Only the latest release receives security fixes. Please check that the issue still exists in the
[latest release](https://github.com/QforA42/MyVNC/releases/latest) (or on `master`) before
reporting.

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions or pull
requests.**

Report them privately through GitHub's
[private vulnerability reporting](https://github.com/QforA42/MyVNC/security/advisories/new)
(the **Security** tab → **Report a vulnerability**). Only the maintainer can see the report.

Please include as much of the following as you can:

- The MyVNC version (the About page, or the file version of `MyVNC.App.exe`) and Windows version.
- The VNC server and version on the other end (e.g. wayvnc 0.9), and the security type in use
  (None, VNC Auth, VeNCrypt Plain/TLSPlain/X509Plain).
- The kind of issue (e.g. credential exposure, memory corruption in a decoder, command injection,
  man-in-the-middle) and its impact.
- Step-by-step reproduction instructions, and a proof of concept if you have one.
- Relevant excerpts from `%APPDATA%\MyVNC\myvnc.log`, **with IP addresses, hostnames and usernames
  removed**.

Never include real passwords, private keys or other secrets in a report.

### What to expect

This is a spare-time project maintained by one person, so timelines are best effort:

- Acknowledgement of your report within about a week.
- An initial assessment (confirmed / not reproducible / not a vulnerability) within about two
  weeks.
- For confirmed issues, a fix in a new patch release, followed by a published GitHub security
  advisory crediting you (unless you prefer to stay anonymous).

Please give a reasonable amount of time for a fix before disclosing the issue publicly.

## Scope

In scope — issues in MyVNC's own code, for example:

- Parsing of data sent by a (possibly malicious) VNC server: the RFB handshake, security
  negotiation, framebuffer encodings (Raw, CopyRect, DesktopSize, ZRLE) and clipboard text.
- How credentials are stored (`%APPDATA%\MyVNC`, protected with Windows DPAPI), logged or
  transmitted.
- Server identity verification: TLS certificate and SSH host key pinning, and the pin store
  (`%APPDATA%\MyVNC\known_hosts.json`) — e.g. a way to get a changed or missing certificate
  accepted without a warning, or credentials sent before the check.
- The SSH terminal shortcut and the SFTP file transfer side channel, including how downloaded
  file names are handled.
- The installer.

Out of scope:

- Vulnerabilities in the VNC server (e.g. wayvnc), the remote operating system, or third-party
  dependencies — please report those upstream. (Do tell us if MyVNC uses a dependency in an
  unsafe way.)
- Attacks that require an attacker who already controls your Windows user account.
- The inherent weaknesses of the RFB protocol itself, such as VNC Auth's DES challenge or the
  `None` security type sending everything unencrypted.

## How MyVNC verifies servers

- **VNC over TLS (VeNCrypt X509Plain/TLSPlain):** preferred over unencrypted VeNCrypt Plain
  whenever the server offers both. wayvnc uses a self-signed certificate, so MyVNC uses
  trust on first use: the first connection shows the certificate's SHA-256 fingerprint for you to
  compare with the server's, and the accepted fingerprint is pinned in
  `%APPDATA%\MyVNC\known_hosts.json`. A changed certificate triggers a warning (defaulting to
  refuse), and a host with a pinned certificate that suddenly offers no TLS is refused as a
  downgrade. Certificates that pass standard CA validation for the host name are accepted
  without a prompt.
- **SFTP file transfer:** the server's SSH host key is verified the same way (trust on first use,
  pinned, warning on change) before the password is sent. Downloaded files keep their remote name
  only if it is a plain file name — anything that could resolve outside the chosen folder (`..`,
  `\`, drive letters, reserved device names such as `CON`) is refused.
- **SSH terminal shortcut:** the host and username are validated against a strict character set
  and passed as separate process arguments — never through a shell-built command string. The
  host key check in that terminal is OpenSSH's own (`~/.ssh/known_hosts`), exactly as when you
  run `ssh` yourself; MyVNC never sends the stored password there.
- The identity check always runs before any credential is sent; rejecting it closes the
  connection at once and stops auto-reconnect.
- Fingerprints are pinned per address and port. A host saved with several addresses (LAN IP,
  FQDN, Tailscale IP/FQDN) is a separate first use for each address you actually connect through.

"Forget saved host keys" on a connection's edit page clears both OpenSSH's `known_hosts` entries
and MyVNC's pinned fingerprints for that host, e.g. after a reinstall.

## Known security limitations

These are known and are not considered new vulnerabilities, but improvements are welcome:

- **Trust on first use is only as good as the first connection.** If the very first connection
  to a host is already intercepted, the attacker's fingerprint gets pinned. Compare the
  fingerprint with the server's (the prompt shows the command to run there) before accepting.
- **VNC Auth and the `None` security type** provide no encryption of the session itself; use
  VeNCrypt with TLS or a secure tunnel such as Tailscale/WireGuard or SSH. The downgrade check
  only protects hosts that have been seen with TLS before.
- **The pin store is an ordinary file in your profile.** Anything running as your Windows user
  can edit `known_hosts.json`, just as it could read your saved (DPAPI-protected) passwords; that
  is outside MyVNC's threat model (see Scope).
