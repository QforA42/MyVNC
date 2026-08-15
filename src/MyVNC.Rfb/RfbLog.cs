namespace MyVNC.Rfb;

/// <summary>
/// A minimal logging hook the RFB library calls into — kept decoupled from any concrete logging
/// implementation (this assembly has no WPF/app dependency) by exposing just a settable sink.
/// The host app wires <see cref="Sink"/> to its own file logger at startup; if nothing is wired,
/// calls are simply no-ops.
/// </summary>
public static class RfbLog
{
    public static Action<string>? Sink { get; set; }

    internal static void Write(string message) => Sink?.Invoke(message);
}
