namespace MyVNC.App.Models;

/// <summary>Which of a connection's up-to-4 addresses to dial.</summary>
public enum AddressKind
{
    HostIp,
    Fqdn,
    TailscaleIp,
    TailscaleFqdn,
}

public static class AddressKindExtensions
{
    public static string LocKey(this AddressKind kind) => kind switch
    {
        AddressKind.Fqdn => "Address.Fqdn",
        AddressKind.TailscaleIp => "Address.TailscaleIp",
        AddressKind.TailscaleFqdn => "Address.TailscaleFqdn",
        _ => "Address.HostIp",
    };
}
