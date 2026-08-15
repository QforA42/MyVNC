using Xunit;

namespace MyVNC.Rfb.Tests;

public class X11KeysymsTests
{
    [Theory]
    [InlineData('a', 0x61u)]
    [InlineData('Z', 0x5Au)]
    [InlineData('0', 0x30u)]
    [InlineData(' ', 0x20u)]
    // Swedish å/ä/ö (Latin-1 0xE5/0xE4/0xF6) — the exact case that broke against wayvnc/wayland
    // keymaps earlier in this project's history; keysym must equal the Latin-1 code point.
    [InlineData('å', 0xE5u)]
    [InlineData('ä', 0xE4u)]
    [InlineData('ö', 0xF6u)]
    public void FromUnicode_Latin1Range_KeysymEqualsCodePoint(char c, uint expected)
        => Assert.Equal(expected, X11Keysyms.FromUnicode(c));

    [Theory]
    [InlineData(0x1F600, 0x0101F600u)] // emoji, outside Latin-1 -> libvncserver "Unicode keysym" convention
    [InlineData(0x0100, 0x01000100u)] // Latin Extended-A, just past the Latin-1 boundary
    public void FromUnicode_OutsideLatin1_UsesUnicodeKeysymConvention(int codepoint, uint expected)
        => Assert.Equal(expected, X11Keysyms.FromUnicode(codepoint));

    [Fact]
    public void FunctionKey_F1ThroughF24_AreSequential()
    {
        for (int n = 1; n <= 24; n++)
            Assert.Equal(X11Keysyms.F1 + (uint)(n - 1), X11Keysyms.FunctionKey(n));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(36)]
    [InlineData(-1)]
    public void FunctionKey_OutOfRange_ReturnsZero(int n)
        => Assert.Equal(0u, X11Keysyms.FunctionKey(n));
}
