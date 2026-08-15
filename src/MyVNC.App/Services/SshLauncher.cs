using System.Diagnostics;
using MyVNC.App.Models;

namespace MyVNC.App.Services;

/// <summary>Launches a local terminal running `ssh user@host` — deliberately does not try to
/// feed the stored VNC password into the SSH prompt (a different auth path, and putting a
/// plaintext password on a process command line is worth avoiding). The user authenticates
/// normally in the spawned terminal, same as running ssh by hand.</summary>
public static class SshLauncher
{
    public static void Launch(string host, string username, SshTerminalChoice choice)
    {
        var target = string.IsNullOrWhiteSpace(username) ? host : $"{username}@{host}";

        switch (choice)
        {
            case SshTerminalChoice.WindowsTerminal:
                TryStart("wt.exe", $"ssh {target}");
                break;
            case SshTerminalChoice.Wsl:
                TryStart("wsl.exe", $"-- ssh {target}");
                break;
            case SshTerminalChoice.PowerShell:
                StartPowerShell(target);
                break;
            case SshTerminalChoice.Auto:
            default:
                if (!TryStart("wt.exe", $"ssh {target}"))
                    StartPowerShell(target);
                break;
        }
    }

    private static void StartPowerShell(string target)
        => TryStart("powershell.exe", $"-NoExit -Command \"ssh {target}\"");

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

    private static bool TryStart(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = true });
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
