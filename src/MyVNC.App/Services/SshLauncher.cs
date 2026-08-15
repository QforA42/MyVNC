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

    private static bool TryStart(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
