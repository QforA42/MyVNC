using System.Windows.Controls;

namespace MyVNC.App.Controls;

/// <summary>Decorative terminal/console-themed backdrop (grid lines, soft accent glow, a large
/// faint ">_" prompt) — shared between the dashboard and session windows so their optional
/// background stays visually identical.</summary>
public partial class TerminalCanvasBackground : UserControl
{
    public TerminalCanvasBackground() => InitializeComponent();
}
