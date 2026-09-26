using System.Diagnostics;
using System.Text.RegularExpressions;
using MyVNC.App.Models;

namespace MyVNC.App.Services;

/// <summary>Launches a local terminal running `ssh user@host` — deliberately does not try to
/// feed the stored VNC password into the SSH prompt (a different auth path, and putting a
/// plaintext password on a process command line is worth avoiding). The user authenticates
/// normally in the spawned terminal, same as running ssh by hand.</summary>
public static class SshLauncher
{
    // Only characters that can legitimately appear in a hostname/IP (v4 or v6, with an optional
    // %zone) or a Linux username. Everything else is refused rather than escaped: the target ends
    // up on a terminal's command line — and for PowerShell, inside a PowerShell command string —
    // so a stored profile containing ';', quotes, spaces or '&' could otherwise run arbitrary
    // commands, and a leading '-' could smuggle in ssh options such as -oProxyCommand=.
    private static readonly Regex HostPattern = new(@"^[A-Za-z0-9.:%_][A-Za-z0-9.:%_-]{0,252}$", RegexOptions.CultureInvariant);
    private static readonly Regex UserPattern = new(@"^[A-Za-z0-9._][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    public static bool IsValidTarget(string host, string? username)
        => HostPattern.IsMatch(host) && (string.IsNullOrWhiteSpace(username) || UserPattern.IsMatch(username));

    /// <summary>Returns false (and launches nothing) if the host or username contains characters
    /// that aren't valid in either — see <see cref="HostPattern"/>.</summary>
    public static bool Launch(string host, string username, SshTerminalChoice choice)
    {
        host = host.Trim();
        username = username?.Trim() ?? string.Empty;
        if (!IsValidTarget(host, username))
        {
            AppLog.Write("SSH terminal not launched: host or username contains invalid characters");
            return false;
        }

        var target = username.Length == 0 ? host : $"{username}@{host}";

        switch (choice)
        {
            case SshTerminalChoice.WindowsTerminal:
                TryStart("wt.exe", "ssh", target);
                break;
            case SshTerminalChoice.Wsl:
                // --exec runs ssh directly instead of through the distro's shell.
                TryStart("wsl.exe", "--exec", "ssh", "--", target);
                break;
            case SshTerminalChoice.PowerShell:
                StartPowerShell(target);
                break;
            case SshTerminalChoice.Auto:
            default:
                if (!TryStart("wt.exe", "ssh", target))
                    StartPowerShell(target);
                break;
        }
        return true;
    }

    // -Command re-parses its argument as PowerShell source, so the (already validated) target is
    // additionally single-quoted there to keep it a literal string.
    private static void StartPowerShell(string target)
        => TryStart("powershell.exe", "-NoExit", "-Command", $"ssh -- '{target}'");

    /// <summary>Removes cached SSH host-key entries for the given addresses from the user's
    /// known_hosts, via `ssh-keygen -R`. A host reappearing with a different key isn't always an
    /// attack — a reinstalled/reimaged machine gets fresh host keys too — so this only clears the
    /// stale entry; it never bypasses verification or auto-trusts whatever key shows up next.</summary>
    public static int ForgetHostKeys(IEnumerable<string> addresses)
    {
        var cleared = 0;
        foreach (var address in addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "ssh-keygen",
                    ArgumentList = { "-R", address },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                process!.WaitForExit(5000);
                AppLog.Write($"ssh-keygen -R {address}: exit {process.ExitCode}");
                cleared++;
            }
            catch (Exception ex)
            {
                AppLog.Write($"ssh-keygen -R {address} failed: {ex.Message}");
            }
        }
        return cleared;
    }

    private static bool TryStart(string fileName, params string[] arguments)
    {
        try
        {
            // ArgumentList quotes each argument individually, so nothing is re-split or
            // re-interpreted by a shell on the way to the terminal. A console program started
            // from this GUI process gets its own new console window.
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
            AppLog.Write($"SSH terminal launched: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            // Not logged as an error — Auto mode deliberately tries Windows Terminal first and
            // falls back to PowerShell, so a failure here can be the expected "not installed" path.
            AppLog.Write($"SSH terminal launch failed for {fileName}: {ex.Message}");
            return false;
        }
    }
}
