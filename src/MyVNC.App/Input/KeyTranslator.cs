using System.Windows.Input;
using MyVNC.Rfb;

namespace MyVNC.App.Input;

/// <summary>
/// Maps WPF <see cref="Key"/> values to X11 keysyms for keys that must be sent
/// on physical KeyDown/KeyUp (navigation, control, function, modifier keys).
/// Printable/character keys are intentionally NOT mapped here — those are sent
/// from WPF's TextInput event instead, so the OS keyboard layout (incl. AltGr,
/// dead keys, IME) resolves the correct character before we transmit it.
/// </summary>
internal static class KeyTranslator
{
    private static readonly Dictionary<Key, uint> Map = new()
    {
        [Key.Back] = X11Keysyms.BackSpace,
        [Key.Tab] = X11Keysyms.Tab,
        [Key.Enter] = X11Keysyms.Return,
        [Key.Escape] = X11Keysyms.Escape,
        [Key.Space] = 0x0020,

        [Key.Delete] = X11Keysyms.Delete,
        [Key.Insert] = X11Keysyms.Insert,
        [Key.Home] = X11Keysyms.Home,
        [Key.End] = X11Keysyms.End,
        [Key.PageUp] = X11Keysyms.PageUp,
        [Key.PageDown] = X11Keysyms.PageDown,

        [Key.Left] = X11Keysyms.Left,
        [Key.Up] = X11Keysyms.Up,
        [Key.Right] = X11Keysyms.Right,
        [Key.Down] = X11Keysyms.Down,

        [Key.Pause] = X11Keysyms.Pause,
        [Key.PrintScreen] = X11Keysyms.PrintScreen,
        [Key.Scroll] = X11Keysyms.ScrollLock,
        [Key.CapsLock] = X11Keysyms.CapsLock,
        [Key.NumLock] = X11Keysyms.NumLock,
        [Key.Apps] = X11Keysyms.Menu,

        [Key.LeftShift] = X11Keysyms.Shift_L,
        [Key.RightShift] = X11Keysyms.Shift_R,
        [Key.LeftCtrl] = X11Keysyms.Control_L,
        [Key.RightCtrl] = X11Keysyms.Control_R,
        [Key.LeftAlt] = X11Keysyms.Alt_L,
        [Key.RightAlt] = X11Keysyms.Alt_R,
        [Key.LWin] = X11Keysyms.Super_L,
        [Key.RWin] = X11Keysyms.Super_R,

        [Key.F1] = X11Keysyms.FunctionKey(1),
        [Key.F2] = X11Keysyms.FunctionKey(2),
        [Key.F3] = X11Keysyms.FunctionKey(3),
        [Key.F4] = X11Keysyms.FunctionKey(4),
        [Key.F5] = X11Keysyms.FunctionKey(5),
        [Key.F6] = X11Keysyms.FunctionKey(6),
        [Key.F7] = X11Keysyms.FunctionKey(7),
        [Key.F8] = X11Keysyms.FunctionKey(8),
        [Key.F9] = X11Keysyms.FunctionKey(9),
        [Key.F10] = X11Keysyms.FunctionKey(10),
        [Key.F11] = X11Keysyms.FunctionKey(11),
        [Key.F12] = X11Keysyms.FunctionKey(12),
        [Key.F13] = X11Keysyms.FunctionKey(13),
        [Key.F14] = X11Keysyms.FunctionKey(14),
        [Key.F15] = X11Keysyms.FunctionKey(15),
        [Key.F16] = X11Keysyms.FunctionKey(16),
        [Key.F17] = X11Keysyms.FunctionKey(17),
        [Key.F18] = X11Keysyms.FunctionKey(18),
        [Key.F19] = X11Keysyms.FunctionKey(19),
        [Key.F20] = X11Keysyms.FunctionKey(20),
        [Key.F21] = X11Keysyms.FunctionKey(21),
        [Key.F22] = X11Keysyms.FunctionKey(22),
        [Key.F23] = X11Keysyms.FunctionKey(23),
        [Key.F24] = X11Keysyms.FunctionKey(24),

        // Numpad — sent as their own keysyms so numlock-off navigation still works.
        [Key.NumPad0] = 0xffb0, [Key.NumPad1] = 0xffb1, [Key.NumPad2] = 0xffb2,
        [Key.NumPad3] = 0xffb3, [Key.NumPad4] = 0xffb4, [Key.NumPad5] = 0xffb5,
        [Key.NumPad6] = 0xffb6, [Key.NumPad7] = 0xffb7, [Key.NumPad8] = 0xffb8,
        [Key.NumPad9] = 0xffb9,
        [Key.Decimal] = 0xffae,
        [Key.Add] = 0xffab,
        [Key.Subtract] = 0xffad,
        [Key.Multiply] = 0xffaa,
        [Key.Divide] = 0xffaf,
    };

    public static bool TryGetKeysym(Key key, out uint keysym) => Map.TryGetValue(key, out keysym);

    // Base (unshifted) US-layout keysyms for letters/digits/common punctuation. Windows never
    // raises TextInput for Ctrl- or Alt-chords (Ctrl+V etc. produce a control character, not
    // the letter), so shortcuts like Ctrl+C/V/Z or Hyprland's Super+<key> binds need these sent
    // as explicit key events instead of relying on the character pipeline.
    private static readonly Dictionary<Key, uint> BaseAsciiMap = new()
    {
        [Key.A] = 'a', [Key.B] = 'b', [Key.C] = 'c', [Key.D] = 'd', [Key.E] = 'e',
        [Key.F] = 'f', [Key.G] = 'g', [Key.H] = 'h', [Key.I] = 'i', [Key.J] = 'j',
        [Key.K] = 'k', [Key.L] = 'l', [Key.M] = 'm', [Key.N] = 'n', [Key.O] = 'o',
        [Key.P] = 'p', [Key.Q] = 'q', [Key.R] = 'r', [Key.S] = 's', [Key.T] = 't',
        [Key.U] = 'u', [Key.V] = 'v', [Key.W] = 'w', [Key.X] = 'x', [Key.Y] = 'y',
        [Key.Z] = 'z',
        [Key.D0] = '0', [Key.D1] = '1', [Key.D2] = '2', [Key.D3] = '3', [Key.D4] = '4',
        [Key.D5] = '5', [Key.D6] = '6', [Key.D7] = '7', [Key.D8] = '8', [Key.D9] = '9',
        [Key.OemComma] = ',', [Key.OemPeriod] = '.', [Key.OemMinus] = '-', [Key.OemPlus] = '=',
    };

    public static bool TryGetBaseAsciiKeysym(Key key, out uint keysym) => BaseAsciiMap.TryGetValue(key, out keysym);

    public static bool IsModifier(Key key) => key is
        Key.LeftShift or Key.RightShift or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;
}
