using Xunit;

namespace MyVNC.Rfb.Tests;

public class BigEndianStreamHelpersTests
{
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)0x1234)]
    [InlineData(ushort.MaxValue)]
    public async Task U16_RoundTrips(ushort value)
    {
        using var ms = new MemoryStream();
        ms.WriteU16(value);
        ms.Position = 0;
        Assert.Equal(value, await ms.ReadU16Async(CancellationToken.None));
    }

    [Theory]
    [InlineData((uint)0)]
    [InlineData((uint)1)]
    [InlineData((uint)0x12345678)]
    [InlineData(uint.MaxValue)]
    public async Task U32_RoundTrips(uint value)
    {
        using var ms = new MemoryStream();
        ms.WriteU32(value);
        ms.Position = 0;
        Assert.Equal(value, await ms.ReadU32Async(CancellationToken.None));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task I32_RoundTrips(int value)
    {
        using var ms = new MemoryStream();
        ms.WriteI32(value);
        ms.Position = 0;
        Assert.Equal(value, await ms.ReadI32Async(CancellationToken.None));
    }

    [Fact]
    public void U16_IsWrittenBigEndian()
    {
        using var ms = new MemoryStream();
        ms.WriteU16(0x1234);
        Assert.Equal([0x12, 0x34], ms.ToArray());
    }

    [Fact]
    public void U32_IsWrittenBigEndian()
    {
        using var ms = new MemoryStream();
        ms.WriteU32(0x12345678);
        Assert.Equal([0x12, 0x34, 0x56, 0x78], ms.ToArray());
    }

    [Fact]
    public async Task ReadExactAsync_ThrowsOnTruncatedStream()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<Exception>(() => ms.ReadExactAsync(10, CancellationToken.None));
    }
}
