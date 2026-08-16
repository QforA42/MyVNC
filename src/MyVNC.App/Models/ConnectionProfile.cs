using System.Text.Json.Serialization;

namespace MyVNC.App.Models;

public sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>The host's plain IP address — the only address that's required; the other
    /// three are optional alternate ways to reach the same machine.</summary>
    public string Host { get; set; } = string.Empty;
    public string Fqdn { get; set; } = string.Empty;
    public string TailscaleIp { get; set; } = string.Empty;
    public string TailscaleFqdn { get; set; } = string.Empty;

    /// <summary>Which address a plain click on "Anslut" uses.</summary>
    public AddressKind DefaultAddress { get; set; } = AddressKind.HostIp;

    /// <summary>Which address was actually used last (default click or an explicit pick from
    /// the dropdown) — taskbar jump list shortcuts replay this one, not necessarily the
    /// configured default.</summary>
    public AddressKind LastUsedAddress { get; set; } = AddressKind.HostIp;

    public int Port { get; set; } = 5900;
    public string Username { get; set; } = string.Empty;

    /// <summary>DPAPI-protected password, base64-encoded. Never store plaintext on disk.</summary>
    public string? ProtectedPassword { get; set; }

    public DateTimeOffset? LastConnected { get; set; }

    /// <summary>Pinned connections always appear in the taskbar jump list (up to 5), ahead of
    /// the most-recently-used ones.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Connect without sending keyboard/mouse/clipboard-to-remote — pure "watch only".</summary>
    public bool ViewOnly { get; set; }
    public bool ReceiveClipboard { get; set; } = true;
    public bool SendClipboard { get; set; } = true;

    /// <summary>Starts the session at native 1:1 pixel size instead of fit-to-window (still
    /// togglable live from the session toolbar).</summary>
    public bool ActualSize { get; set; }

    /// <summary>Whether port 22 was last found open on this host — never persisted, re-checked
    /// each time the dashboard loads or regains focus. Only gates the SSH icon's visibility.</summary>
    [JsonIgnore]
    public bool IsSshAvailable { get; set; }

    /// <summary>Whether a session to this profile (via any of its known addresses) is already
    /// open somewhere — never persisted, re-checked each time the dashboard loads or regains
    /// focus. Disables the Connect button so it's obvious at a glance rather than only finding
    /// out after clicking and getting redirected to the existing session.</summary>
    [JsonIgnore]
    public bool IsSessionActive { get; set; }

    public string GetAddress(AddressKind kind) => kind switch
    {
        AddressKind.Fqdn => Fqdn,
        AddressKind.TailscaleIp => TailscaleIp,
        AddressKind.TailscaleFqdn => TailscaleFqdn,
        _ => Host,
    };

    /// <summary>Resolves a kind to a usable address, falling back to the host IP if that
    /// particular field was left blank (e.g. default is set to FQDN but none was entered).</summary>
    public string ResolveAddress(AddressKind kind)
    {
        var address = GetAddress(kind);
        return string.IsNullOrWhiteSpace(address) ? Host : address;
    }

    /// <summary>All non-empty addresses, in a fixed display order, each with its kind and label key.</summary>
    public IEnumerable<(AddressKind Kind, string Value)> AvailableAddresses()
    {
        if (!string.IsNullOrWhiteSpace(Host)) yield return (AddressKind.HostIp, Host);
        if (!string.IsNullOrWhiteSpace(Fqdn)) yield return (AddressKind.Fqdn, Fqdn);
        if (!string.IsNullOrWhiteSpace(TailscaleIp)) yield return (AddressKind.TailscaleIp, TailscaleIp);
        if (!string.IsNullOrWhiteSpace(TailscaleFqdn)) yield return (AddressKind.TailscaleFqdn, TailscaleFqdn);
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? $"{ResolveAddress(DefaultAddress)}:{Port}" : Name;
    public string Subtitle => $"{ResolveAddress(DefaultAddress)}:{Port}";
}
