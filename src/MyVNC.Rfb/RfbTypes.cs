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
}

public enum RfbConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}
