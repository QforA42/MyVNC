using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVNC.App.Interop;

/// <summary>Renders the app's ">_"-mark logo into a bitmap for use as a Window.Icon (title bar,
/// taskbar, Alt-Tab) — built from the same vector shapes as <see cref="Controls.AppLogo"/>
/// rather than a static .ico asset, so there's nothing to keep in sync if the mark changes.</summary>
internal static class AppIconFactory
{
    private static ImageSource? _cached;

    public static ImageSource Create()
    {
        if (_cached is not null) return _cached;

        const double size = 64;
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x5B, 0x8C, 0xFF),
                Color.FromRgb(0x7C, 0x5C, 0xFF),
                new Point(0, 0), new Point(1, 1)),
            Child = new TextBlock
            {
                Text = ">_",
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 30,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        };

        border.Measure(new Size(size, size));
        border.Arrange(new Rect(0, 0, size, size));

        var rtb = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(border);
        rtb.Freeze();

        _cached = rtb;
        return rtb;
    }
}
