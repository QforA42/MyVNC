using System.IO.Compression;

namespace MyVNC.Rfb;

/// <summary>
/// A growable, append-only byte buffer exposed as a <see cref="Stream"/> so a single persistent
/// <see cref="ZLibStream"/> can decompress across many ZRLE rectangles. ZRLE uses one continuous
/// zlib stream for the whole connection (later rectangles can back-reference earlier ones), but
/// the network delivers it in per-rectangle, length-prefixed chunks — this stream lets us feed
/// those chunks in as they arrive while a single inflate context keeps running underneath.
/// </summary>
internal sealed class ZrleFeederStream : Stream
{
    private byte[] _buf = new byte[4096];
    private int _writePos;
    private int _readPos;

    public void Feed(byte[] data)
    {
        if (_writePos + data.Length > _buf.Length)
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _writePos + data.Length));
        Array.Copy(data, 0, _buf, _writePos, data.Length);
        _writePos += data.Length;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = _writePos - _readPos;
        var n = Math.Min(available, count);
        if (n > 0)
        {
            Array.Copy(_buf, _readPos, buffer, offset, n);
            _readPos += n;
        }
        if (_readPos == _writePos) _readPos = _writePos = 0; // fully drained: reclaim the buffer
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Decodes ZRLE (Zlib Run-Length Encoding) rectangles: the whole rectangle is zlib-compressed,
/// then split into 64x64 tiles, each independently run-length- or palette-encoded. Pixels are
/// carried as "CPIXEL" — 3 bytes (B,G,R) — since our negotiated pixel format is 32bpp/depth-24,
/// which conveniently matches the framebuffer's own per-pixel BGR layout (just missing the
/// unused 4th padding byte that WPF's Bgr32 format ignores).
/// </summary>
internal sealed class ZrleDecoder
{
    private const int TileSize = 64;

    private readonly ZrleFeederStream _feeder = new();
    private readonly ZLibStream _inflate;

    public ZrleDecoder() => _inflate = new ZLibStream(_feeder, CompressionMode.Decompress, leaveOpen: true);

    /// <summary>Decompresses and decodes one rectangle's worth of compressed tile data into a
    /// row-major B,G,R,pad byte array of length w*h*4.</summary>
    public byte[] DecodeRectangle(byte[] compressed, int w, int h)
    {
        _feeder.Feed(compressed);

        var pixels = new byte[w * h * 4];
        for (int tileY = 0; tileY < h; tileY += TileSize)
        {
            var th = Math.Min(TileSize, h - tileY);
            for (int tileX = 0; tileX < w; tileX += TileSize)
            {
                var tw = Math.Min(TileSize, w - tileX);
                DecodeTile(pixels, w, tileX, tileY, tw, th);
            }
        }
        return pixels;
    }

    private void DecodeTile(byte[] pixels, int rectWidth, int tileX, int tileY, int tw, int th)
    {
        var subencoding = ReadByte();
        switch (subencoding)
        {
            case 0: // Raw
                for (int ty = 0; ty < th; ty++)
                    for (int tx = 0; tx < tw; tx++)
                        WritePixel(pixels, rectWidth, tileX + tx, tileY + ty, ReadCPixel());
                break;

            case 1: // Solid colour
                {
                    var px = ReadCPixel();
                    for (int ty = 0; ty < th; ty++)
                        for (int tx = 0; tx < tw; tx++)
                            WritePixel(pixels, rectWidth, tileX + tx, tileY + ty, px);
                    break;
                }

            case >= 2 and <= 16: // Packed palette
                {
                    var palette = ReadPalette(subencoding);
                    var bitsPerIndex = subencoding == 2 ? 1 : subencoding <= 4 ? 2 : 4;
                    var rowBytes = (tw * bitsPerIndex + 7) / 8;
                    for (int ty = 0; ty < th; ty++)
                    {
                        var row = ReadBytes(rowBytes);
                        for (int tx = 0; tx < tw; tx++)
                            WritePixel(pixels, rectWidth, tileX + tx, tileY + ty, palette[ReadPackedIndex(row, tx, bitsPerIndex)]);
                    }
                    break;
                }

            case 128: // Plain RLE
                {
                    var total = tw * th;
                    var written = 0;
                    while (written < total)
                    {
                        var px = ReadCPixel();
                        var run = ReadRunLength();
                        for (var i = 0; i < run && written < total; i++, written++)
                            WritePixel(pixels, rectWidth, tileX + written % tw, tileY + written / tw, px);
                    }
                    break;
                }

            case >= 130: // Palette RLE
                {
                    var palette = ReadPalette(subencoding - 128);
                    var total = tw * th;
                    var written = 0;
                    while (written < total)
                    {
                        var indexByte = ReadByte();
                        var px = palette[indexByte & 0x7F];
                        var run = (indexByte & 0x80) != 0 ? ReadRunLength() : 1;
                        for (var i = 0; i < run && written < total; i++, written++)
                            WritePixel(pixels, rectWidth, tileX + written % tw, tileY + written / tw, px);
                    }
                    break;
                }

            default:
                throw new IOException($"Okänd ZRLE-tile-kodning ({subencoding}).");
        }
    }

    private byte[][] ReadPalette(int size)
    {
        var palette = new byte[size][];
        for (int i = 0; i < size; i++) palette[i] = ReadCPixel();
        return palette;
    }

    private static int ReadPackedIndex(byte[] row, int pixelIndex, int bitsPerIndex)
    {
        var pixelsPerByte = 8 / bitsPerIndex;
        var byteIndex = pixelIndex / pixelsPerByte;
        var posInByte = pixelIndex % pixelsPerByte;
        var shift = 8 - bitsPerIndex - posInByte * bitsPerIndex;
        return (row[byteIndex] >> shift) & ((1 << bitsPerIndex) - 1);
    }

    /// <summary>Run lengths are encoded as a sequence of bytes: 255 means "add 255 and keep
    /// reading", any other value ends the run and is added too. Actual length = 1 + that sum.</summary>
    private int ReadRunLength()
    {
        int length = 0;
        byte b;
        do
        {
            b = ReadByte();
            length += b;
        } while (b == 255);
        return length + 1;
    }

    // Reused for every ReadByte/ReadCPixel call instead of allocating a fresh array each time —
    // Plain/Palette RLE tiles on a busy screen can call these thousands of times per rectangle,
    // and that allocation churn showed up as real, measured CPU cost (a live incident traced
    // sustained 50%+ CPU of one core partly to this, on a large/actively-used 2560x1440 session).
    private readonly byte[] _scratch3 = new byte[3];

    private byte[] ReadCPixel()
    {
        ReadBytesInto(_scratch3, 3);
        return [_scratch3[0], _scratch3[1], _scratch3[2], 0];
    }

    private static void WritePixel(byte[] pixels, int rectWidth, int x, int y, byte[] px)
    {
        var offset = (y * rectWidth + x) * 4;
        pixels[offset] = px[0];
        pixels[offset + 1] = px[1];
        pixels[offset + 2] = px[2];
        pixels[offset + 3] = px[3];
    }

    private byte ReadByte()
    {
        ReadBytesInto(_scratch3, 1);
        return _scratch3[0];
    }

    private void ReadBytesInto(byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = _inflate.Read(buffer, read, count - read);
            if (n <= 0) throw new IOException("ZRLE-strömmen tog slut oväntat.");
            read += n;
        }
    }

    private byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = _inflate.Read(buffer, read, count - read);
            if (n <= 0) throw new IOException("ZRLE-strömmen tog slut oväntat.");
            read += n;
        }
        return buffer;
    }

    public void Dispose() => _inflate.Dispose();
}
