namespace MyVNC.Rfb;

/// <summary>Well-known X11 keysym values (X11/keysymdef.h) needed for RFB key events.</summary>
public static class X11Keysyms
{
    public const uint BackSpace = 0xff08;
    public const uint Tab = 0xff09;
    public const uint Space = 0x0020;
    public const uint Return = 0xff0d;
    public const uint Escape = 0xff1b;
    public const uint Delete = 0xffff;
    public const uint Insert = 0xff63;
    public const uint Home = 0xff50;
    public const uint End = 0xff57;
    public const uint PageUp = 0xff55;
    public const uint PageDown = 0xff56;
    public const uint Left = 0xff51;
    public const uint Up = 0xff52;
    public const uint Right = 0xff53;
    public const uint Down = 0xff54;
    public const uint Pause = 0xff13;
    public const uint PrintScreen = 0xff61;
    public const uint ScrollLock = 0xff14;
    public const uint CapsLock = 0xffe5;
    public const uint NumLock = 0xff7f;
    public const uint Menu = 0xff67;

    public const uint Shift_L = 0xffe1;
    public const uint Shift_R = 0xffe2;
    public const uint Control_L = 0xffe3;
    public const uint Control_R = 0xffe4;
    public const uint Alt_L = 0xffe9;
    public const uint Alt_R = 0xffea;
    public const uint AltGr = 0xfe03; // ISO_Level3_Shift
    public const uint Super_L = 0xffeb;
    public const uint Super_R = 0xffec;

    public const uint F1 = 0xffbe;
    public const uint F13 = F1 + 12;
    public const uint F24 = F1 + 23;

    public static uint FunctionKey(int n) => n is >= 1 and <= 35 ? (uint)(F1 + (n - 1)) : 0;

    /// <summary>Maps a Unicode code point to the corresponding X11 keysym.</summary>
    public static uint FromUnicode(int codepoint)
    {
        // Latin-1 range: keysym equals code point (0x0020-0x00FF map directly, excluding control chars).
        if (codepoint is >= 0x0020 and <= 0x00ff)
            return (uint)codepoint;

        // RFC 3.8 / libvncserver "Unicode keysym" convention: 0x01000000 + code point.
        return 0x01000000u + (uint)codepoint;
    }
}
