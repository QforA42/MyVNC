using System.Windows.Media;
using Microsoft.Win32;
using MyVNC.App.Models;

namespace MyVNC.App.Interop;

/// <summary>Reads the accent color the user picked in Windows (Settings → Personalization →
/// Colors) and derives the app's accent ramp from it, so MyVNC matches the rest of the desktop
/// instead of always painting its own blue.</summary>
internal static class SystemAccent
{
    /// <summary>The accent shades a theme needs: the base color plus its hover/pressed states and
    /// the text/glyph color that stays readable on top of an accent-filled surface.</summary>
    internal readonly record struct Ramp(Color Accent, Color Hover, Color Pressed, Color Foreground);

    // The accent doubles as text and icon color on the app's own surfaces (the "Connect" action,
    // the pin/SSH glyphs), so a raw accent that happens to be near-black on a dark theme — or
    // near-white on a light one — has to be pulled back toward the readable side first.
    private const double MinTextContrast = 4.5;
    private static readonly Color DarkThemeSurface = Color.FromRgb(0x1D, 0x21, 0x29);   // BgCardColor (dark)
    private static readonly Color LightThemeSurface = Color.FromRgb(0xFF, 0xFF, 0xFF);  // BgCardColor (light)
    private static readonly Color OnAccentDark = Color.FromRgb(0x0B, 0x0E, 0x14);
    private static readonly Color OnAccentLight = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Returns the accent ramp for <paramref name="theme"/>, or null when Windows has no
    /// accent color to read — the caller then keeps the palette's own built-in accent.</summary>
    public static Ramp? Resolve(AppTheme theme)
    {
        var baseColor = ReadWindowsAccent();
        if (baseColor is null) return null;

        var surface = theme == AppTheme.Light ? LightThemeSurface : DarkThemeSurface;
        var accent = EnsureContrast(baseColor.Value, surface, MinTextContrast);

        // Hover lifts, pressed sinks — the same direction the hand-picked palettes used, and both
        // clamped so an already-extreme accent can't run off the end of the lightness range.
        var hover = Shift(accent, theme == AppTheme.Light ? +0.10 : +0.08);
        var pressed = Shift(accent, -0.10);
        var foreground = Contrast(OnAccentLight, accent) >= Contrast(OnAccentDark, accent)
            ? OnAccentLight
            : OnAccentDark;

        return new Ramp(accent, hover, pressed, foreground);
    }

    /// <summary>DWM's AccentColor is the exact color the user picked (the palette under
    /// Explorer\Accent is a generated ramp around it), stored as 0xAABBGGRR.</summary>
    private static Color? ReadWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr) return null;
            return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }
        catch
        {
            // Key missing or unreadable (locked-down profile) — caller falls back to the built-in accent.
            return null;
        }
    }

    /// <summary>Walks <paramref name="color"/>'s lightness away from <paramref name="against"/>
    /// until it clears <paramref name="minRatio"/>, keeping hue and saturation intact.</summary>
    private static Color EnsureContrast(Color color, Color against, double minRatio)
    {
        if (Contrast(color, against) >= minRatio) return color;

        // Move away from the surface: lighten on a dark surface, darken on a light one.
        double direction = Luminance(against) < 0.5 ? +0.02 : -0.02;
        var candidate = color;
        for (int i = 0; i < 50 && Contrast(candidate, against) < minRatio; i++)
        {
            var next = Shift(candidate, direction);
            if (next == candidate) break; // hit pure white/black — as far as it goes
            candidate = next;
        }
        return candidate;
    }

    private static Color Shift(Color color, double deltaLightness)
    {
        var (h, s, l) = ToHsl(color);
        return FromHsl(h, s, Math.Clamp(l + deltaLightness, 0, 1));
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>WCAG relative luminance.</summary>
    private static double Luminance(Color c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;

        if (max - min > 1e-9)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        if (s <= 1e-9)
        {
            byte v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromRgb(Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                 : t < 1.0 / 2 ? q
                 : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                 : p;
        return (byte)Math.Round(v * 255);
    }
}
