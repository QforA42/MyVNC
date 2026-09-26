using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MyVNC.Rfb;

/// <summary>
/// A minimal RFB (VNC) protocol client. Supports RFB 3.3/3.7/3.8 handshakes,
/// None/VNC-Auth security, a 32bpp true-colour pixel format, and the
/// Raw + CopyRect encodings (universally supported by every RFB server,
/// including wayvnc on omarchy/Hyprland).
/// </summary>
public sealed class RfbClient : IAsyncDisposable
{
    private const int EncodingRaw = 0;
    private const int EncodingCopyRect = 1;
    private const int EncodingZrle = 16;
    private const int EncodingDesktopSize = -223;

    private TcpClient? _tcp;
    private Stream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private ZrleDecoder? _zrleDecoder;

    private readonly byte[] _framebuffer0 = Array.Empty<byte>();
    private byte[] _framebuffer = Array.Empty<byte>();
    private readonly Lock _fbLock = new();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public string DesktopName { get; private set; } = string.Empty;
    public RfbConnectionState State { get; private set; } = RfbConnectionState.Disconnected;

    public event EventHandler<FramebufferUpdateEventArgs>? FramebufferUpdated;
    public event EventHandler<DesktopResizedEventArgs>? DesktopResized;
    public event EventHandler<ServerCutTextEventArgs>? ServerCutTextReceived;
    public event EventHandler? BellReceived;
    public event EventHandler<Exception>? ConnectionLost;

    /// <summary>
    /// Performs the TCP connect and RFB handshake only. On return, <see cref="Width"/>/<see cref="Height"/>
    /// are known but no framebuffer data is flowing yet — call <see cref="BeginReceiving"/> once the caller
    /// is ready to render (e.g. after allocating a same-sized bitmap), otherwise the very first full-screen
    /// update can race ahead of that setup and get silently dropped.
    /// </summary>
    public async Task ConnectAsync(RfbConnectionOptions options, CancellationToken ct = default)
    {
        // Never log options.Username/Password — only connection metadata that's actually useful
        // for reproducing a protocol-level problem without needing the credentials themselves.
        RfbLog.Write($"Connecting to {options.Host}:{options.Port}...");
        try
        {
            State = RfbConnectionState.Connecting;
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(options.Host, options.Port, ct).ConfigureAwait(false);
            _stream = _tcp.GetStream();

            await DoHandshakeAsync(options, ct).ConfigureAwait(false);

            State = RfbConnectionState.Connected;
            RfbLog.Write($"Connected: {Width}x{Height} desktop='{DesktopName}'");
        }
        catch (Exception ex)
        {
            RfbLog.Write($"Connect failed for {options.Host}:{options.Port}: {ex}");
            // Close the socket now rather than whenever the caller next disposes this client:
            // a half-finished handshake (e.g. a rejected server identity) otherwise stays open
            // and can hold the only slot on a single-client wayvnc server.
            _stream?.Dispose();
            _tcp?.Dispose();
            State = RfbConnectionState.Disconnected;
            throw;
        }
    }

    /// <summary>Starts the receive loop and requests the first full-screen update. See <see cref="ConnectAsync"/>.</summary>
    public void BeginReceiving(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);

        RequestFramebufferUpdate(incremental: false);
    }

    private async Task DoHandshakeAsync(RfbConnectionOptions options, CancellationToken ct)
    {
        var stream = _stream!;

        var serverVersion = await stream.ReadExactAsync(12, ct).ConfigureAwait(false);
        var versionStr = Encoding.ASCII.GetString(serverVersion).TrimEnd('\n', '\0', ' ');
        // We always speak 3.8 back; every server in practice accepts this.
        var clientVersion = Encoding.ASCII.GetBytes("RFB 003.008\n");
        await stream.WriteAsync(clientVersion, ct).ConfigureAwait(false);

        bool isLegacy33 = versionStr.Contains("003.003");

        RfbSecurityType chosen;
        if (isLegacy33)
        {
            var secType = await stream.ReadU32Async(ct).ConfigureAwait(false);
            chosen = (RfbSecurityType)secType;
            if (chosen == RfbSecurityType.Invalid)
                throw new IOException("Servern avvisade anslutningen (ingen giltig säkerhetstyp).");
        }
        else
        {
            var count = await stream.ReadU8Async(ct).ConfigureAwait(false);
            if (count == 0)
            {
                var reason = await ReadRfbStringAsync(stream, ct).ConfigureAwait(false);
                throw new IOException($"Servern avvisade anslutningen: {reason}");
            }

            var types = new RfbSecurityType[count];
            for (int i = 0; i < count; i++)
                types[i] = (RfbSecurityType)await stream.ReadU8Async(ct).ConfigureAwait(false);

            chosen = types.Contains(RfbSecurityType.VeNCrypt) ? RfbSecurityType.VeNCrypt
                : types.Contains(RfbSecurityType.None) ? RfbSecurityType.None
                : types.Contains(RfbSecurityType.VncAuth) ? RfbSecurityType.VncAuth
                : throw new NotSupportedException("Servern kräver en säkerhetstyp som inte stöds (endast None/VNC-lösenord/VeNCrypt stöds).");

            stream.WriteU8((byte)chosen);
        }

        RfbLog.Write($"Server version '{versionStr}', security type: {chosen}");

        // VeNCrypt verifies inside DoVeNCryptAsync, once it knows whether TLS is in use.
        if (chosen is RfbSecurityType.None or RfbSecurityType.VncAuth)
            await VerifyServerIdentityAsync(options, chosen.ToString(), certificate: null, chainValid: false, ct).ConfigureAwait(false);

        if (chosen == RfbSecurityType.VncAuth)
        {
            if (string.IsNullOrEmpty(options.Password))
                throw new InvalidOperationException("Servern kräver ett lösenord.");

            var challenge = await stream.ReadExactAsync(16, ct).ConfigureAwait(false);
            var response = VncAuth.EncryptChallenge(options.Password, challenge);
            await stream.WriteAsync(response, ct).ConfigureAwait(false);
        }
        else if (chosen == RfbSecurityType.VeNCrypt)
        {
            stream = await DoVeNCryptAsync(stream, options, ct).ConfigureAwait(false);
            _stream = stream;
        }

        if (!isLegacy33 || chosen != RfbSecurityType.None)
        {
            var result = await stream.ReadU32Async(ct).ConfigureAwait(false);
            if (result != 0)
            {
                string reason = "Autentisering misslyckades.";
                try { reason = await ReadRfbStringAsync(stream, ct).ConfigureAwait(false); } catch { /* older servers omit this */ }
                throw new IOException(reason);
            }
        }

        // ClientInit: shared-flag = 1 (allow other viewers to stay connected).
        stream.WriteU8(1);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // ServerInit.
        Width = await stream.ReadU16Async(ct).ConfigureAwait(false);
        Height = await stream.ReadU16Async(ct).ConfigureAwait(false);
        _ = await stream.ReadExactAsync(16, ct).ConfigureAwait(false); // server's pixel format, we override it below.
        var nameLen = await stream.ReadU32Async(ct).ConfigureAwait(false);
        var nameBytes = nameLen > 0 ? await stream.ReadExactAsync((int)nameLen, ct).ConfigureAwait(false) : Array.Empty<byte>();
        DesktopName = string.IsNullOrEmpty(options.DesktopNameOverride)
            ? Encoding.UTF8.GetString(nameBytes)
            : options.DesktopNameOverride;

        _framebuffer = new byte[Width * Height * 4];

        SendSetPixelFormat();
        SendSetEncodings();
    }

    /// <summary>
    /// VeNCrypt (RFB security type 19) is what wayvnc and most modern Wayland/Hyprland
    /// compositors use for username+password auth. Negotiates protocol version 0.2, picks
    /// a username/password sub-type (TLS-wrapped X509Plain/TLSPlain preferred over Plain), then sends the
    /// credentials in the clear over that channel.
    /// </summary>
    private static async Task<Stream> DoVeNCryptAsync(Stream stream, RfbConnectionOptions options, CancellationToken ct)
    {
        var major = await stream.ReadU8Async(ct).ConfigureAwait(false);
        var minor = await stream.ReadU8Async(ct).ConfigureAwait(false);
        _ = major; _ = minor;

        stream.WriteU8(0); // we only support VeNCrypt 0.2
        stream.WriteU8(2);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var status = await stream.ReadU8Async(ct).ConfigureAwait(false);
        if (status != 0)
            throw new NotSupportedException("Servern stöder inte VeNCrypt 0.2.");

        var subtypeCount = await stream.ReadU8Async(ct).ConfigureAwait(false);
        if (subtypeCount == 0)
            throw new NotSupportedException("Servern erbjöd inga VeNCrypt-underttyper.");

        var subtypes = new VeNCryptSubType[subtypeCount];
        for (int i = 0; i < subtypeCount; i++)
            subtypes[i] = (VeNCryptSubType)await stream.ReadU32Async(ct).ConfigureAwait(false);

        // Prefer the TLS-wrapped variants: Plain sends the password in the clear, so picking it
        // whenever it's offered would let anyone on the path read it, and a man-in-the-middle
        // could strip TLS simply by offering Plain alongside it.
        var chosenSubtype = subtypes.Contains(VeNCryptSubType.X509Plain) ? VeNCryptSubType.X509Plain
            : subtypes.Contains(VeNCryptSubType.TLSPlain) ? VeNCryptSubType.TLSPlain
            : subtypes.Contains(VeNCryptSubType.Plain) ? VeNCryptSubType.Plain
            : throw new NotSupportedException("Servern kräver en VeNCrypt-underttyp som inte stöds (endast användarnamn/lösenord stöds).");

        RfbLog.Write($"VeNCrypt subtype: {chosenSubtype}");

        stream.WriteU32((uint)chosenSubtype);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // The server acknowledges the chosen sub-type with a single byte (1 = ok) before
        // continuing — easy to miss, but skipping it desyncs the stream and makes the
        // subsequent TLS handshake fail with a "corrupted frame" error.
        var subtypeAck = await stream.ReadU8Async(ct).ConfigureAwait(false);
        if (subtypeAck != 1)
            throw new NotSupportedException("Servern accepterade inte den valda VeNCrypt-underttypen.");

        if (chosenSubtype is VeNCryptSubType.X509Plain or VeNCryptSubType.TLSPlain)
        {
            // wayvnc's default certificate is self-signed and not part of any CA chain, so the
            // handshake itself accepts any certificate and records whether standard validation
            // would have passed. The actual trust decision (a pinned fingerprint, trust on first
            // use) is VerifyServerIdentity's, made below before the credentials go out.
            var policyErrors = SslPolicyErrors.None;
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, errors) =>
            {
                policyErrors = errors;
                return true;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = options.Host,
                EnabledSslProtocols = SslProtocols.None, // let the OS pick the best mutually supported protocol
            }, ct).ConfigureAwait(false);
            stream = ssl;

            if (ssl.RemoteCertificate is null)
                throw new AuthenticationException("The server did not present a TLS certificate.");
            var certificate = ssl.RemoteCertificate as X509Certificate2
                ?? X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate.GetRawCertData());

            await VerifyServerIdentityAsync(options, $"VeNCrypt {chosenSubtype}", certificate,
                chainValid: policyErrors == SslPolicyErrors.None, ct).ConfigureAwait(false);
        }
        else
        {
            await VerifyServerIdentityAsync(options, $"VeNCrypt {chosenSubtype}", certificate: null, chainValid: false, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(options.Username))
            throw new InvalidOperationException("Servern kräver ett användarnamn.");

        var userBytes = Encoding.UTF8.GetBytes(options.Username);
        var passBytes = Encoding.UTF8.GetBytes(options.Password ?? string.Empty);
        stream.WriteU32((uint)userBytes.Length);
        stream.WriteU32((uint)passBytes.Length);
        stream.Write(userBytes);
        stream.Write(passBytes);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        return stream;
    }

    private static async Task VerifyServerIdentityAsync(RfbConnectionOptions options, string security,
        X509Certificate2? certificate, bool chainValid, CancellationToken ct)
    {
        var identity = new RfbServerIdentity
        {
            Host = options.Host,
            Port = options.Port,
            Security = security,
            Certificate = certificate,
            CertificateSha256 = certificate is null ? null : FormatFingerprint(certificate.GetCertHash(HashAlgorithmName.SHA256)),
            CertificateChainValid = certificate is not null && chainValid,
        };
        RfbLog.Write($"Server identity: {security}, certificate SHA-256 {identity.CertificateSha256 ?? "(none)"}, chain valid: {identity.CertificateChainValid}");

        if (options.VerifyServerIdentity is { } verify && !await verify(identity, ct).ConfigureAwait(false))
            throw new RfbServerIdentityRejectedException(identity);
    }

    internal static string FormatFingerprint(byte[] hash) => Convert.ToHexString(hash).Chunk(2)
        .Select(pair => new string(pair)).Aggregate((a, b) => $"{a}:{b}");

    private static async Task<string> ReadRfbStringAsync(Stream stream, CancellationToken ct)
    {
        var len = await stream.ReadU32Async(ct).ConfigureAwait(false);
        var bytes = len > 0 ? await stream.ReadExactAsync((int)len, ct).ConfigureAwait(false) : Array.Empty<byte>();
        return Encoding.UTF8.GetString(bytes);
    }

    private void SendSetPixelFormat()
    {
        var stream = _stream!;
        stream.WriteU8(0); // message-type: SetPixelFormat
        stream.WriteU8(0); stream.WriteU8(0); stream.WriteU8(0); // padding
        stream.WriteU8(32); // bits-per-pixel
        stream.WriteU8(24); // depth
        stream.WriteU8(0);  // big-endian-flag = false
        stream.WriteU8(1);  // true-colour-flag = true
        stream.WriteU16(255); // red-max
        stream.WriteU16(255); // green-max
        stream.WriteU16(255); // blue-max
        stream.WriteU8(16); // red-shift
        stream.WriteU8(8);  // green-shift
        stream.WriteU8(0);  // blue-shift
        stream.WriteU8(0); stream.WriteU8(0); stream.WriteU8(0); // padding
        stream.Flush();
    }

    private void SendSetEncodings()
    {
        var stream = _stream!;
        // Listed in preference order — servers try encodings top-to-bottom, so ZRLE (much less
        // bandwidth over a WAN/Tailscale link) goes first, with Raw as the universal fallback.
        int[] encodings = [EncodingZrle, EncodingCopyRect, EncodingDesktopSize, EncodingRaw];
        stream.WriteU8(2); // message-type: SetEncodings
        stream.WriteU8(0); // padding
        stream.WriteU16((ushort)encodings.Length);
        foreach (var e in encodings)
            stream.WriteI32(e);
        stream.Flush();
    }

    // All four send methods below can race a connection that's dying or just died: the receive
    // loop detects a drop asynchronously and raises ConnectionLost, but a send can already be
    // in-flight when the socket goes away, throwing IOException/SocketException. That's not
    // actionable by the caller — the receive loop's disconnect handling is the correct, only
    // authoritative path for "this connection is dead" — so catch and log rather than let it
    // propagate. Unhandled, this previously crashed the whole app from inside a raw Win32
    // clipboard-change callback with no surrounding try/catch of its own.
    public void RequestFramebufferUpdate(bool incremental)
    {
        if (_stream is null) return;
        try
        {
            lock (_fbLock)
            {
                var stream = _stream;
                stream.WriteU8(3); // message-type: FramebufferUpdateRequest
                stream.WriteU8((byte)(incremental ? 1 : 0));
                stream.WriteU16(0);
                stream.WriteU16(0);
                stream.WriteU16((ushort)Width);
                stream.WriteU16((ushort)Height);
                stream.Flush();
            }
        }
        catch (Exception ex)
        {
            RfbLog.Write($"RequestFramebufferUpdate failed (connection likely closing): {ex.Message}");
        }
    }

    public void SendKeyEvent(uint keysym, bool down)
    {
        if (_stream is null) return;
        try
        {
            lock (_fbLock)
            {
                var stream = _stream;
                stream.WriteU8(4); // message-type: KeyEvent
                stream.WriteU8((byte)(down ? 1 : 0));
                stream.WriteU16(0); // padding
                stream.WriteU32(keysym);
                stream.Flush();
            }
        }
        catch (Exception ex)
        {
            RfbLog.Write($"SendKeyEvent failed (connection likely closing): {ex.Message}");
        }
    }

    public void SendPointerEvent(int x, int y, int buttonMask)
    {
        if (_stream is null) return;
        x = Math.Clamp(x, 0, Math.Max(0, Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, Height - 1));
        try
        {
            lock (_fbLock)
            {
                var stream = _stream;
                stream.WriteU8(5); // message-type: PointerEvent
                stream.WriteU8((byte)buttonMask);
                stream.WriteU16((ushort)x);
                stream.WriteU16((ushort)y);
                stream.Flush();
            }
        }
        catch (Exception ex)
        {
            RfbLog.Write($"SendPointerEvent failed (connection likely closing): {ex.Message}");
        }
    }

    public void SendClientCutText(string text)
    {
        if (_stream is null) return;
        try
        {
            var bytes = Encoding.Latin1.GetBytes(text.Replace("\r\n", "\n"));
            lock (_fbLock)
            {
                var stream = _stream;
                stream.WriteU8(6); // message-type: ClientCutText
                stream.WriteU8(0); stream.WriteU8(0); stream.WriteU8(0); // padding
                stream.WriteU32((uint)bytes.Length);
                stream.Write(bytes);
                stream.Flush();
            }
        }
        catch (Exception ex)
        {
            RfbLog.Write($"SendClientCutText failed (connection likely closing): {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var stream = _stream!;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var messageType = await stream.ReadU8Async(ct).ConfigureAwait(false);
                switch (messageType)
                {
                    case 0:
                        await HandleFramebufferUpdateAsync(ct).ConfigureAwait(false);
                        break;
                    case 1: // SetColourMapEntries — not used with true-colour; skip payload.
                        _ = await stream.ReadU8Async(ct).ConfigureAwait(false); // padding
                        _ = await stream.ReadU16Async(ct).ConfigureAwait(false); // first-colour
                        var n = await stream.ReadU16Async(ct).ConfigureAwait(false);
                        _ = await stream.ReadExactAsync(n * 6, ct).ConfigureAwait(false);
                        break;
                    case 2: // Bell
                        BellReceived?.Invoke(this, EventArgs.Empty);
                        break;
                    case 3: // ServerCutText
                        _ = await stream.ReadExactAsync(3, ct).ConfigureAwait(false); // padding
                        var len = await stream.ReadU32Async(ct).ConfigureAwait(false);
                        var textBytes = await stream.ReadExactAsync((int)len, ct).ConfigureAwait(false);
                        ServerCutTextReceived?.Invoke(this, new ServerCutTextEventArgs { Text = Encoding.Latin1.GetString(textBytes) });
                        break;
                    default:
                        throw new IOException($"Okänt meddelande från servern (typ {messageType}).");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            RfbLog.Write($"Connection lost ({_tcp?.Client?.RemoteEndPoint}): {ex}");
            State = RfbConnectionState.Failed;
            ConnectionLost?.Invoke(this, ex);
        }
    }

    private async Task HandleFramebufferUpdateAsync(CancellationToken ct)
    {
        var stream = _stream!;
        _ = await stream.ReadU8Async(ct).ConfigureAwait(false); // padding
        var numRects = await stream.ReadU16Async(ct).ConfigureAwait(false);
        var didResize = false;

        for (int i = 0; i < numRects; i++)
        {
            var x = await stream.ReadU16Async(ct).ConfigureAwait(false);
            var y = await stream.ReadU16Async(ct).ConfigureAwait(false);
            var w = await stream.ReadU16Async(ct).ConfigureAwait(false);
            var h = await stream.ReadU16Async(ct).ConfigureAwait(false);
            var encoding = await stream.ReadI32Async(ct).ConfigureAwait(false);

            switch (encoding)
            {
                case EncodingRaw:
                    await HandleRawRectAsync(x, y, w, h, ct).ConfigureAwait(false);
                    break;
                case EncodingCopyRect:
                    await HandleCopyRectAsync(x, y, w, h, ct).ConfigureAwait(false);
                    break;
                case EncodingZrle:
                    await HandleZrleRectAsync(x, y, w, h, ct).ConfigureAwait(false);
                    break;
                case EncodingDesktopSize:
                    HandleDesktopResize(w, h);
                    didResize = true;
                    break;
                default:
                    throw new IOException($"Servern skickade en okänd kodning ({encoding}).");
            }
        }

        // An incremental request only asks for what changed from the server's point of view —
        // it has no idea the client just reallocated a blank framebuffer for the new dimensions
        // in HandleDesktopResize. Requesting incremental right after a resize can leave parts of
        // the new framebuffer never painted (server-side "nothing changed there" vs. client-side
        // "I have nothing there at all"), which reads as a gray/blank screen after a resolution
        // change. A full (non-incremental) request forces the server to resend everything.
        RequestFramebufferUpdate(incremental: !didResize);
    }

    private async Task HandleRawRectAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        if (w <= 0 || h <= 0) return;
        var rowBytes = w * 4;
        var data = await _stream!.ReadExactAsync(rowBytes * h, ct).ConfigureAwait(false);

        lock (_fbLock)
        {
            for (int row = 0; row < h; row++)
            {
                int destOffset = ((y + row) * Width + x) * 4;
                Array.Copy(data, row * rowBytes, _framebuffer, destOffset, rowBytes);
            }
        }

        FramebufferUpdated?.Invoke(this, new FramebufferUpdateEventArgs
        {
            Rectangle = new RfbRectangle((ushort)x, (ushort)y, (ushort)w, (ushort)h),
            Pixels = data,
        });
    }

    private Task HandleCopyRectAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        return CopyRectCoreAsync(x, y, w, h, ct);
    }

    private async Task CopyRectCoreAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        var srcX = await _stream!.ReadU16Async(ct).ConfigureAwait(false);
        var srcY = await _stream!.ReadU16Async(ct).ConfigureAwait(false);
        if (w <= 0 || h <= 0) return;

        byte[] extracted = new byte[w * h * 4];
        lock (_fbLock)
        {
            // Copy row-by-row via a temp buffer to correctly handle overlapping regions.
            var temp = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
            {
                int srcOffset = ((srcY + row) * Width + srcX) * 4;
                Array.Copy(_framebuffer, srcOffset, temp, row * w * 4, w * 4);
            }
            for (int row = 0; row < h; row++)
            {
                int destOffset = ((y + row) * Width + x) * 4;
                Array.Copy(temp, row * w * 4, _framebuffer, destOffset, w * 4);
            }
            Array.Copy(temp, extracted, temp.Length);
        }

        FramebufferUpdated?.Invoke(this, new FramebufferUpdateEventArgs
        {
            Rectangle = new RfbRectangle((ushort)x, (ushort)y, (ushort)w, (ushort)h),
            Pixels = extracted,
        });
    }

    private async Task HandleZrleRectAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        var length = await _stream!.ReadU32Async(ct).ConfigureAwait(false);
        var compressed = await _stream!.ReadExactAsync((int)length, ct).ConfigureAwait(false);
        if (w <= 0 || h <= 0) return;

        // One zlib stream spans the whole connection (later tiles can reference earlier ones),
        // so the decoder — and the inflate context inside it — is created once and reused.
        _zrleDecoder ??= new ZrleDecoder();
        var pixels = _zrleDecoder.DecodeRectangle(compressed, w, h);

        lock (_fbLock)
        {
            for (int row = 0; row < h; row++)
            {
                int destOffset = ((y + row) * Width + x) * 4;
                Array.Copy(pixels, row * w * 4, _framebuffer, destOffset, w * 4);
            }
        }

        FramebufferUpdated?.Invoke(this, new FramebufferUpdateEventArgs
        {
            Rectangle = new RfbRectangle((ushort)x, (ushort)y, (ushort)w, (ushort)h),
            Pixels = pixels,
        });
    }

    private void HandleDesktopResize(int newWidth, int newHeight)
    {
        lock (_fbLock)
        {
            Width = newWidth;
            Height = newHeight;
            _framebuffer = new byte[Width * Height * 4];
        }
        DesktopResized?.Invoke(this, new DesktopResizedEventArgs { Width = newWidth, Height = newHeight });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();

            // Closing a viewer must release its server-side client slot immediately.  Merely
            // cancelling the token is not sufficient here: a NetworkStream read can remain
            // blocked briefly while the peer is sending a framebuffer update.  During that
            // window a just-opened replacement viewer can be rejected by a single-client
            // wayvnc guard, or both receive loops can spend CPU decoding frames.  Disposing the
            // transport makes the outstanding read complete now; the receive loop treats the
            // resulting cancellation/object-disposed exception as normal shutdown.
            _stream?.Dispose();
            _tcp?.Dispose();

            if (_receiveLoop is not null)
                await Task.WhenAny(_receiveLoop, Task.Delay(500)).ConfigureAwait(false);
        }
        catch { /* best-effort shutdown */ }
        finally
        {
            _zrleDecoder?.Dispose();
            _zrleDecoder = null;
            _stream = null;
            _tcp = null;
            State = RfbConnectionState.Disconnected;
        }
    }
}
