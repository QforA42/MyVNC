namespace MyVNC.App.Models;

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Swedish;
    public SessionOpenMode SessionOpenMode { get; set; } = SessionOpenMode.Window;

    /// <summary>Automatically retry with exponential backoff after an unexpected disconnect
    /// (network blip, host reboot). Does not apply to a manual "Disconnect" click.</summary>
    public bool AutoReconnect { get; set; } = true;
}
