using System.Buffers.Binary;

namespace MyVNC.Rfb;

internal static class BigEndianStreamHelpers
{
    public static async Task<byte[]> ReadExactAsync(this Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, 0, count, ct).ConfigureAwait(false);
        return buffer;
    }

    public static async Task<byte> ReadU8Async(this Stream stream, CancellationToken ct)
    {
        var b = await stream.ReadExactAsync(1, ct).ConfigureAwait(false);
        return b[0];
    }

    public static async Task<ushort> ReadU16Async(this Stream stream, CancellationToken ct)
    {
        var b = await stream.ReadExactAsync(2, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    public static async Task<short> ReadI16Async(this Stream stream, CancellationToken ct)
    {
        var b = await stream.ReadExactAsync(2, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt16BigEndian(b);
    }

    public static async Task<uint> ReadU32Async(this Stream stream, CancellationToken ct)
    {
        var b = await stream.ReadExactAsync(4, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    public static async Task<int> ReadI32Async(this Stream stream, CancellationToken ct)
    {
        var b = await stream.ReadExactAsync(4, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32BigEndian(b);
    }

    public static void WriteU8(this Stream stream, byte value) => stream.WriteByte(value);

    public static void WriteU16(this Stream stream, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        stream.Write(b);
    }

    public static void WriteI16(this Stream stream, short value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, value);
        stream.Write(b);
    }

    public static void WriteU32(this Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    public static void WriteI32(this Stream stream, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        stream.Write(b);
    }
}
