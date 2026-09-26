using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace MyVNC.Rfb.Tests;

/// <summary>
/// Drives RfbClient's handshake against a scripted fake server on loopback, to pin down the
/// security-relevant ordering: TLS-wrapped VeNCrypt is preferred over Plain, and the identity
/// check runs — and can abort — before any credential leaves the client.
/// </summary>
public class ServerIdentityTests
{
    private const byte SecurityNone = 1;
    private const byte SecurityVeNCrypt = 19;
    private const uint VeNCryptPlain = 256;
    private const uint VeNCryptX509Plain = 262;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void FormatFingerprint_IsColonSeparatedUppercaseHex()
    {
        Assert.Equal("AB:01:FF", RfbClient.FormatFingerprint([0xAB, 0x01, 0xFF]));
    }

    [Fact]
    public async Task SecurityNone_Accepted_ReportsUnencryptedIdentityAndConnects()
    {
        using var listener = StartListener(out var port);
        RfbServerIdentity? seen = null;

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await HandshakeVersionAsync(s);
            await s.WriteAsync(new byte[] { 1, SecurityNone });
            Assert.Equal(SecurityNone, await ReadU8Async(s));
            await s.WriteAsync(U32(0)); // SecurityResult: OK
            Assert.Equal(1, await ReadU8Async(s)); // ClientInit shared-flag
            await s.WriteAsync(ServerInit(width: 64, height: 48));
            await ReadExactAsync(s, 20 + 4 + 4 * 4); // SetPixelFormat + SetEncodings (4 encodings)
        });

        await using var client = new RfbClient();
        await client.ConnectAsync(Options(port, (identity, _) =>
        {
            seen = identity;
            return Task.FromResult(true);
        })).WaitAsync(Timeout);
        await server.WaitAsync(Timeout);

        Assert.NotNull(seen);
        Assert.Equal("None", seen.Security);
        Assert.False(seen.IsEncrypted);
        Assert.Null(seen.CertificateSha256);
        Assert.Equal(64, client.Width);
    }

    [Fact]
    public async Task SecurityNone_Rejected_ThrowsAndSendsNothingFurther()
    {
        using var listener = StartListener(out var port);

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await HandshakeVersionAsync(s);
            await s.WriteAsync(new byte[] { 1, SecurityNone });
            Assert.Equal(SecurityNone, await ReadU8Async(s));
            return await ReadToEndAsync(s);
        });

        var client = new RfbClient();
        await Assert.ThrowsAsync<RfbServerIdentityRejectedException>(() =>
            client.ConnectAsync(Options(port, (_, _) => Task.FromResult(false))).WaitAsync(Timeout));
        await client.DisposeAsync();

        Assert.Empty(await server.WaitAsync(Timeout)); // no ClientInit after a rejection
    }

    [Fact]
    public async Task Rejection_ClosesTheSocketWithoutWaitingForDispose()
    {
        using var listener = StartListener(out var port);

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await HandshakeVersionAsync(s);
            await s.WriteAsync(new byte[] { 1, SecurityNone });
            Assert.Equal(SecurityNone, await ReadU8Async(s));
            return await ReadToEndAsync(s); // completes only once the client closes the socket
        });

        var client = new RfbClient(); // deliberately not disposed until after the server saw EOF
        await Assert.ThrowsAsync<RfbServerIdentityRejectedException>(() =>
            client.ConnectAsync(Options(port, (_, _) => Task.FromResult(false))).WaitAsync(Timeout));

        Assert.Empty(await server.WaitAsync(Timeout));
        Assert.Equal(RfbConnectionState.Disconnected, client.State);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task VeNCrypt_PrefersX509PlainOverPlain()
    {
        using var listener = StartListener(out var port);

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await NegotiateVeNCryptAsync(s, VeNCryptPlain, VeNCryptX509Plain);
            return await ReadU32Async(s); // the sub-type the client picked
        });

        await using var client = new RfbClient();
        var connect = client.ConnectAsync(Options(port, (_, _) => Task.FromResult(true)));

        Assert.Equal(VeNCryptX509Plain, await server.WaitAsync(Timeout));
        // The fake server hangs up instead of starting TLS, so the connect itself fails.
        await Assert.ThrowsAnyAsync<Exception>(() => connect.WaitAsync(Timeout));
    }

    [Fact]
    public async Task VeNCryptPlain_Rejected_NeverSendsCredentials()
    {
        using var listener = StartListener(out var port);
        RfbServerIdentity? seen = null;

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await NegotiateVeNCryptAsync(s, VeNCryptPlain);
            Assert.Equal(VeNCryptPlain, await ReadU32Async(s));
            await s.WriteAsync(new byte[] { 1 }); // sub-type ack
            return await ReadToEndAsync(s);
        });

        var client = new RfbClient();
        await Assert.ThrowsAsync<RfbServerIdentityRejectedException>(() =>
            client.ConnectAsync(Options(port, (identity, _) =>
            {
                seen = identity;
                return Task.FromResult(false);
            })).WaitAsync(Timeout));
        await client.DisposeAsync();

        var afterAck = await server.WaitAsync(Timeout);
        Assert.Empty(afterAck);
        Assert.DoesNotContain("s3cret", Encoding.UTF8.GetString(afterAck));
        Assert.NotNull(seen);
        Assert.Equal("VeNCrypt Plain", seen.Security);
        Assert.False(seen.IsEncrypted);
    }

    [Fact]
    public async Task VeNCryptX509Plain_SelfSigned_ReportsFingerprintAndRejectionSendsNoCredentials()
    {
        using var listener = StartListener(out var port);
        using var certificate = CreateSelfSignedCertificate();
        RfbServerIdentity? seen = null;

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var s = socket.GetStream();
            await NegotiateVeNCryptAsync(s, VeNCryptX509Plain);
            Assert.Equal(VeNCryptX509Plain, await ReadU32Async(s));
            await s.WriteAsync(new byte[] { 1 }); // sub-type ack
            using var ssl = new SslStream(s);
            await ssl.AuthenticateAsServerAsync(certificate);
            return await ReadToEndAsync(ssl);
        });

        var client = new RfbClient();
        await Assert.ThrowsAsync<RfbServerIdentityRejectedException>(() =>
            client.ConnectAsync(Options(port, (identity, _) =>
            {
                seen = identity;
                return Task.FromResult(false);
            })).WaitAsync(Timeout));
        await client.DisposeAsync();

        Assert.Empty(await server.WaitAsync(Timeout));
        Assert.NotNull(seen);
        Assert.True(seen.IsEncrypted);
        Assert.False(seen.CertificateChainValid);
        Assert.Equal("VeNCrypt X509Plain", seen.Security);
        Assert.Equal(RfbClient.FormatFingerprint(certificate.GetCertHash(HashAlgorithmName.SHA256)), seen.CertificateSha256);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=myvnc-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel can't serve from an ephemeral key; round-trip through PFX to get a usable one.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null);
    }

    private static RfbConnectionOptions Options(int port, Func<RfbServerIdentity, CancellationToken, Task<bool>> verify) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        Username = "alice",
        Password = "s3cret",
        VerifyServerIdentity = verify,
    };

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static async Task HandshakeVersionAsync(Stream s)
    {
        await s.WriteAsync(Encoding.ASCII.GetBytes("RFB 003.008\n"));
        await ReadExactAsync(s, 12);
    }

    private static async Task NegotiateVeNCryptAsync(Stream s, params uint[] subtypes)
    {
        await HandshakeVersionAsync(s);
        await s.WriteAsync(new byte[] { 1, SecurityVeNCrypt });
        Assert.Equal(SecurityVeNCrypt, await ReadU8Async(s));
        await s.WriteAsync(new byte[] { 0, 2 });           // server VeNCrypt version 0.2
        Assert.Equal(new byte[] { 0, 2 }, await ReadExactAsync(s, 2));
        await s.WriteAsync(new byte[] { 0, (byte)subtypes.Length }); // status OK, sub-type count
        foreach (var subtype in subtypes)
            await s.WriteAsync(U32(subtype));
    }

    private static byte[] ServerInit(ushort width, ushort height)
    {
        var buffer = new byte[2 + 2 + 16 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, width);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), height);
        return buffer; // pixel format (ignored by the client) and a zero-length name
    }

    private static byte[] U32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    private static async Task<byte> ReadU8Async(Stream s) => (await ReadExactAsync(s, 1))[0];

    private static async Task<uint> ReadU32Async(Stream s) => BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(s, 4));

    private static async Task<byte[]> ReadExactAsync(Stream s, int count)
    {
        var buffer = new byte[count];
        await s.ReadExactlyAsync(buffer);
        return buffer;
    }

    private static async Task<byte[]> ReadToEndAsync(Stream s)
    {
        using var ms = new MemoryStream();
        try { await s.CopyToAsync(ms); }
        catch (IOException) { /* reset by the client closing — still means "nothing more sent" */ }
        return ms.ToArray();
    }
}
