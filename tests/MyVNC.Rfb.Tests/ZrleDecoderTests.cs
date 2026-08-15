using System.IO.Compression;
using Xunit;

namespace MyVNC.Rfb.Tests;

/// <summary>Exercises every ZRLE tile subencoding against synthetic (hand-built, then
/// zlib-compressed) tile data — this decoder had never been verified against known-correct
/// input/output before, only against a live server where correctness couldn't be isolated.</summary>
public class ZrleDecoderTests
{
    private static byte[] Compress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    private static byte[] CPixel(byte b, byte g, byte r) => [b, g, r];

    private static void AssertPixel(byte[] pixels, int rectWidth, int x, int y, byte b, byte g, byte r)
    {
        var offset = (y * rectWidth + x) * 4;
        Assert.Equal(b, pixels[offset]);
        Assert.Equal(g, pixels[offset + 1]);
        Assert.Equal(r, pixels[offset + 2]);
        Assert.Equal(0, pixels[offset + 3]);
    }

    [Fact]
    public void Solid_FillsWholeTileWithOneColor()
    {
        List<byte> raw = [1, .. CPixel(10, 20, 30)]; // subencoding 1 = solid
        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 4, 4);

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                AssertPixel(pixels, 4, x, y, 10, 20, 30);
    }

    [Fact]
    public void Raw_ReadsEachPixelInOrder()
    {
        List<byte> raw = [0]; // subencoding 0 = raw
        for (int i = 0; i < 3 * 2; i++) // 3x2 tile
            raw.AddRange(CPixel((byte)(i * 10), (byte)(i * 10 + 1), (byte)(i * 10 + 2)));

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 3, 2);

        int idx = 0;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++, idx++)
                AssertPixel(pixels, 3, x, y, (byte)(idx * 10), (byte)(idx * 10 + 1), (byte)(idx * 10 + 2));
    }

    [Fact]
    public void PackedPalette_TwoColors_OneBitPerIndex()
    {
        // 4x2 tile, palette of 2 (1 bit/index): row bytes are packed MSB-first, byte-aligned per row.
        // Row 0: indices 0,1,0,1 -> bits 0101 -> 0b0101_0000 = 0x50
        // Row 1: indices 1,1,0,0 -> bits 1100 -> 0b1100_0000 = 0xC0
        List<byte> raw = [2]; // subencoding 2 = packed palette, size 2
        raw.AddRange(CPixel(1, 1, 1));   // palette[0]
        raw.AddRange(CPixel(9, 9, 9));   // palette[1]
        raw.Add(0x50);
        raw.Add(0xC0);

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 4, 2);

        AssertPixel(pixels, 4, 0, 0, 1, 1, 1);
        AssertPixel(pixels, 4, 1, 0, 9, 9, 9);
        AssertPixel(pixels, 4, 2, 0, 1, 1, 1);
        AssertPixel(pixels, 4, 3, 0, 9, 9, 9);
        AssertPixel(pixels, 4, 0, 1, 9, 9, 9);
        AssertPixel(pixels, 4, 1, 1, 9, 9, 9);
        AssertPixel(pixels, 4, 2, 1, 1, 1, 1);
        AssertPixel(pixels, 4, 3, 1, 1, 1, 1);
    }

    [Fact]
    public void PlainRle_RepeatsColorForRunLength()
    {
        // 6x1 tile: first 4 pixels one color (run length 4 = byte value 3), last 2 another (run length 2 = byte value 1).
        List<byte> raw = [128]; // subencoding 128 = plain RLE
        raw.AddRange(CPixel(5, 5, 5));
        raw.Add(3); // run length = 3 + 1 = 4
        raw.AddRange(CPixel(8, 8, 8));
        raw.Add(1); // run length = 1 + 1 = 2

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 6, 1);

        for (int x = 0; x < 4; x++) AssertPixel(pixels, 6, x, 0, 5, 5, 5);
        for (int x = 4; x < 6; x++) AssertPixel(pixels, 6, x, 0, 8, 8, 8);
    }

    [Fact]
    public void PlainRle_LongRun_ChainsBytesAbove255()
    {
        // A run of 300 needs two length bytes: 255 (continue) then 44 (255+44+1=300).
        List<byte> raw = [128];
        raw.AddRange(CPixel(7, 7, 7));
        raw.Add(255);
        raw.Add(44);

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 20, 15); // 300 pixels total

        for (int i = 0; i < 300; i++)
            AssertPixel(pixels, 20, i % 20, i / 20, 7, 7, 7);
    }

    [Fact]
    public void PaletteRle_HighBitMarksRunLength_ClearBitMeansSinglePixel()
    {
        // 4x1 tile, palette size 2 (subencoding 130). Index 0 with high bit set = a run;
        // index 1 without the high bit = a single pixel.
        List<byte> raw = [130];
        raw.AddRange(CPixel(2, 2, 2));   // palette[0]
        raw.AddRange(CPixel(4, 4, 4));   // palette[1]
        raw.Add(0x80);                  // index 0, run follows
        raw.Add(2);                     // run length = 2 + 1 = 3
        raw.Add(0x01);                  // index 1, no run byte (single pixel)

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 4, 1);

        AssertPixel(pixels, 4, 0, 0, 2, 2, 2);
        AssertPixel(pixels, 4, 1, 0, 2, 2, 2);
        AssertPixel(pixels, 4, 2, 0, 2, 2, 2);
        AssertPixel(pixels, 4, 3, 0, 4, 4, 4);
    }

    [Fact]
    public void TileGrid_SplitsRectangleIntoUpTo64x64Tiles()
    {
        // 68x68 rectangle: 2x2 grid of tiles (three of them clipped to 4px). Each tile solid,
        // a different color, to confirm the grid math (tile origin/size) is right at the edges.
        List<byte> raw = [];
        void AddSolidTile(byte b, byte g, byte r) { raw.Add(1); raw.AddRange(CPixel(b, g, r)); }
        AddSolidTile(1, 0, 0);   // tile (0,0), 64x64
        AddSolidTile(2, 0, 0);   // tile (64,0), 4x64
        AddSolidTile(3, 0, 0);   // tile (0,64), 64x4
        AddSolidTile(4, 0, 0);   // tile (64,64), 4x4

        var pixels = new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 68, 68);

        AssertPixel(pixels, 68, 0, 0, 1, 0, 0);
        AssertPixel(pixels, 68, 63, 63, 1, 0, 0);
        AssertPixel(pixels, 68, 64, 0, 2, 0, 0);
        AssertPixel(pixels, 68, 67, 63, 2, 0, 0);
        AssertPixel(pixels, 68, 0, 64, 3, 0, 0);
        AssertPixel(pixels, 68, 63, 67, 3, 0, 0);
        AssertPixel(pixels, 68, 64, 64, 4, 0, 0);
        AssertPixel(pixels, 68, 67, 67, 4, 0, 0);
    }

    [Fact]
    public void SameDecoder_DecodesConsecutiveRectangles_FromOneContinuousZlibStream()
    {
        // ZRLE uses one continuous zlib stream for the whole connection — a decoder must be able
        // to decode a second rectangle using leftover bytes from the first Feed call, without a
        // fresh zlib header. This is the exact scenario RfbClient relies on across framebuffer updates.
        List<byte> raw1 = [1, .. CPixel(11, 22, 33)];
        List<byte> raw2 = [1, .. CPixel(44, 55, 66)];
        var combined = Compress([.. raw1, .. raw2]);

        var decoder = new ZrleDecoder();
        var pixels1 = decoder.DecodeRectangle(combined, 2, 2);
        var pixels2 = decoder.DecodeRectangle([], 2, 2); // no new bytes: must use what's already buffered

        AssertPixel(pixels1, 2, 0, 0, 11, 22, 33);
        AssertPixel(pixels2, 2, 0, 0, 44, 55, 66);
    }

    [Fact]
    public void UnknownSubencoding_ThrowsIOException()
    {
        List<byte> raw = [200]; // not a defined subencoding
        var ex = Record.Exception(() => new ZrleDecoder().DecodeRectangle(Compress([.. raw]), 4, 4));
        Assert.IsType<IOException>(ex);
    }
}
