namespace MyVNC.App.Models;

/// <summary>How a new "Anslut" opens a session relative to any already open.</summary>
public enum SessionOpenMode
{
    /// <summary>Every session gets its own top-level window.</summary>
    Window,

    /// <summary>Sessions are tabs inside one shared window (new window only if none is open).</summary>
    Tab,
}
