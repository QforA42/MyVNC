namespace MyVNC.App.Models;

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Swedish;

    /// <summary>Light/dark palette, or "System" to follow the Windows app mode live. The accent
    /// color always comes from Windows regardless of this. See Services.ThemeManager.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public SessionOpenMode SessionOpenMode { get; set; } = SessionOpenMode.Window;

    /// <summary>Automatically retry with exponential backoff after an unexpected disconnect
    /// (network blip, host reboot). Does not apply to a manual "Disconnect" click.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Terminal app launched by a connection card's SSH icon (only shown once that
    /// host's port 22 is confirmed reachable).</summary>
    public SshTerminalChoice SshTerminal { get; set; } = SshTerminalChoice.Auto;

    /// <summary>Off by default — writes protocol/app diagnostics (never credentials, keystrokes,
    /// or clipboard contents) to %APPDATA%\MyVNC\myvnc.log, so a problem can be debugged from
    /// the log alone. See Services.AppLog.</summary>
    public bool DebugLogging { get; set; }
}
