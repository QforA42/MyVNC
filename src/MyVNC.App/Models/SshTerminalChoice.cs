namespace MyVNC.App.Models;

/// <summary>Which terminal app the SSH icon on a connection card launches.</summary>
public enum SshTerminalChoice
{
    /// <summary>Windows Terminal if available, otherwise PowerShell.</summary>
    Auto,
    PowerShell,
    WindowsTerminal,
    Wsl,
}

public static class SshTerminalChoiceExtensions
{
    public static string LocKey(this SshTerminalChoice choice) => choice switch
    {
        SshTerminalChoice.PowerShell => "SshTerminal.PowerShell",
        SshTerminalChoice.WindowsTerminal => "SshTerminal.WindowsTerminal",
        SshTerminalChoice.Wsl => "SshTerminal.Wsl",
        _ => "SshTerminal.Auto",
    };
}
