using System.Security.Cryptography.X509Certificates;

namespace MyVNC.Rfb;

public enum RfbSecurityType : byte
{
    Invalid = 0,
    None = 1,
    VncAuth = 2,
    Tight = 16,
    VeNCrypt = 19,
}

/// <summary>VeNCrypt sub-types (RFB VeNCrypt extension). Values &gt;= 260 wrap the connection in TLS first.</summary>
internal enum VeNCryptSubType : uint
{
    Plain = 256,
    TLSNone = 257,
    TLSVnc = 258,
    TLSPlain = 259,
    X509None = 260,
    X509Vnc = 261,
    X509Plain = 262,
}

public readonly record struct RfbRectangle(ushort X, ushort Y, ushort Width, ushort Height);

public sealed class FramebufferUpdateEventArgs : EventArgs
{
    public required RfbRectangle Rectangle { get; init; }
    /// <summary>BGRA32 pixel data, top-down, tightly packed (Width*Height*4 bytes).</summary>
    public required byte[] Pixels { get; init; }
}

public sealed class DesktopResizedEventArgs : EventArgs
{
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public sealed class ServerCutTextEventArgs : EventArgs
{
    public required string Text { get; init; }
}

public sealed class RfbConnectionOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 5900;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string DesktopNameOverride { get; init; } = string.Empty;

    /// <summary>Client-side input policy — not part of the RFB handshake, but carried here
    /// since every connection call site already threads an options object through.</summary>
    public bool ViewOnly { get; init; }
    public bool ReceiveClipboard { get; init; } = true;
    public bool SendClipboard { get; init; } = true;
    public bool ActualSize { get; init; }

    /// <summary>Called once per connection after the security type is negotiated (and, for
    /// VeNCrypt X509/TLS, after the TLS handshake) but before any credential is sent. Returning
    /// false aborts the connection with <see cref="RfbServerIdentityRejectedException"/>. When
    /// null, every server is accepted — the library itself has no trust store.</summary>
    public Func<RfbServerIdentity, CancellationToken, Task<bool>>? VerifyServerIdentity { get; init; }
}

/// <summary>What the client knows about the server's identity at the point credentials are
/// about to be sent.</summary>
public sealed class RfbServerIdentity
{
    public required string Host { get; init; }
    public required int Port { get; init; }

    /// <summary>The negotiated security, e.g. "VeNCrypt X509Plain", "VncAuth" or "None".</summary>
    public required string Security { get; init; }

    /// <summary>The server's TLS certificate, or null when the connection is not TLS-protected
    /// (security type None/VncAuth, or VeNCrypt Plain).</summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>SHA-256 fingerprint of <see cref="Certificate"/> as colon-separated hex, or null.</summary>
    public string? CertificateSha256 { get; init; }

    /// <summary>True when the certificate chains to a trusted root and matches <see cref="Host"/>
    /// (standard TLS validation succeeded). A self-signed wayvnc certificate is never valid.</summary>
    public bool CertificateChainValid { get; init; }

    public bool IsEncrypted => Certificate is not null;
}

/// <summary>Thrown when <see cref="RfbConnectionOptions.VerifyServerIdentity"/> rejects the server.
/// Not a transient network error — reconnecting will be rejected the same way.</summary>
public sealed class RfbServerIdentityRejectedException(RfbServerIdentity identity)
    : IOException($"Server identity for {identity.Host}:{identity.Port} was rejected ({identity.Security}).")
{
    public RfbServerIdentity Identity { get; } = identity;
}

public enum RfbConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}
