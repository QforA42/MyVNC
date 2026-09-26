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

- The MyVNC version (Help/About page, or the file version of `MyVNC.App.exe`) and Windows version.
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
- The SSH terminal shortcut and the SFTP file transfer side channel.
- The installer.

Out of scope:

- Vulnerabilities in the VNC server (e.g. wayvnc), the remote operating system, or third-party
  dependencies — please report those upstream. (Do tell us if MyVNC uses a dependency in an
  unsafe way.)
- Attacks that require an attacker who already controls your Windows user account.
- The inherent weaknesses of the RFB protocol itself, such as VNC Auth's DES challenge or the
  `None` security type sending everything unencrypted.

## Known security limitations

These are known and are not considered new vulnerabilities, but fixes are welcome:

- **VeNCrypt TLS certificates are not verified.** When connecting with TLSPlain/X509Plain, MyVNC
  accepts any server certificate (VNC servers typically use self-signed ones). An attacker who can
  intercept the connection can therefore impersonate the server and capture the username and
  password. Mitigation: only connect over a network you trust or an encrypted overlay such as
  Tailscale/WireGuard or an SSH tunnel.
- **The SFTP file transfer does not verify the SSH host key.** The same credentials are sent over
  SSH, with the same man-in-the-middle exposure and the same mitigation.
- **VNC Auth and the `None` security type** provide no encryption of the session itself; use
  VeNCrypt with TLS or a secure tunnel.
