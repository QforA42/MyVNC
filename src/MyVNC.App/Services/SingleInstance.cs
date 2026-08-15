using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MyVNC.App.Services;

/// <summary>
/// Ensures only one MyVNC process runs at a time. A second launch (desktop icon while already
/// open, a taskbar jump-list shortcut, Start menu) forwards its command-line arguments to the
/// already-running instance over a named pipe and exits immediately — otherwise "open new
/// sessions as tabs" mode is unreachable from a jump-list shortcut, since a fresh process has
/// its own empty <see cref="SessionWindow.Shared"/> and can never join the running window's tabs.
/// </summary>
public static class SingleInstance
{
    private const string PipeName = "MyVNC-Activation";

    // Profile IDs are hex GUIDs and the only other argument is the literal "--connect", so a
    // plain pipe character is a safe, always-visible separator for the one-line handshake below.
    private const char ArgSeparator = '|';

    /// <summary>Sends this process's arguments to the already-running instance. Best-effort —
    /// if the pipe can't be reached the caller still exits; there's nothing more useful to do.</summary>
    public static void ForwardToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(string.Join(ArgSeparator, args));
        }
        catch
        {
            // Best-effort; the running instance may be busy shutting down, or unreachable.
        }
    }

    /// <summary>Starts a background loop accepting activation requests from later launches of
    /// this app, for as long as the process lives.</summary>
    public static void StartListening(Action<string[]> onActivationReceived)
    {
        var thread = new Thread(() => ListenLoop(onActivationReceived)) { IsBackground = true };
        thread.Start();
    }

    private static void ListenLoop(Action<string[]> onActivationReceived)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = reader.ReadLine();
                var args = string.IsNullOrEmpty(line) ? [] : line.Split(ArgSeparator);
                AppLog.Write($"Activation received: {(args.Length > 0 ? string.Join(' ', args) : "(none)")}");
                onActivationReceived(args);
            }
            catch (Exception ex)
            {
                // Keep listening — one failed connection shouldn't stop future activations. But
                // if pipe creation itself is what's failing (e.g. this pipe name is somehow
                // already owned elsewhere), retrying with no backoff turns into a tight infinite
                // loop that pegs a CPU core and allocates as fast as it can spin — a real
                // multi-GB-in-seconds runaway was traced back to exactly this. Pace retries, and
                // log unconditionally (not gated by Settings.DebugLogging) since this exact spot
                // is the one that already caused a real incident once.
                AppLog.WriteAlways($"SingleInstance pipe listener failed, retrying in 1s: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }
}
