namespace MyVNC.App.Models;

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Swedish;
    public SessionOpenMode SessionOpenMode { get; set; } = SessionOpenMode.Window;

    /// <summary>Automatically retry with exponential backoff after an unexpected disconnect
    /// (network blip, host reboot). Does not apply to a manual "Disconnect" click.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Terminal app launched by a connection card's SSH icon (only shown once that
    /// host's port 22 is confirmed reachable).</summary>
    public SshTerminalChoice SshTerminal { get; set; } = SshTerminalChoice.Auto;
}
