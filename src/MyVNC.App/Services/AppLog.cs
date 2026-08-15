using System.IO;

namespace MyVNC.App.Services;

/// <summary>
/// A simple, opt-in file logger — off by default, toggled from Settings. Exists so a problem
/// can be debugged/reproduced/fixed from the log alone, without needing hands-on access to
/// whichever machine MyVNC is running on.
///
/// Never write credentials, keystrokes, or clipboard contents here — only enough protocol/app
/// metadata (host, port, encoding/security types, exception details) to diagnose a problem. The
/// log is meant to be pasted/shared for debugging, so treat every line as something the user
/// might send to someone else.
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();
    private const long MaxBytes = 5 * 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyVNC", "myvnc.log");

    private static bool _enabled;
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Write(value ? "--- Debug logging enabled ---" : "--- Debug logging disabled ---");
        }
    }

    public static bool LogFileExists() => File.Exists(FilePath);

    public static void Write(string message)
    {
        if (!_enabled && message != "--- Debug logging disabled ---") return;
        AppendLine(message);
    }

    public static void WriteException(string context, Exception ex) => Write($"{context}: {ex}");

    /// <summary>Bypasses the Settings.DebugLogging opt-in — reserved for ResourceWatchdog's
    /// memory/CPU-amok warnings. Those are exactly the evidence needed after a crash, and the
    /// user may not have thought to turn logging on before the app started misbehaving.</summary>
    public static void WriteAlways(string message) => AppendLine(message);

    private static void AppendLine(string message)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                TrimIfTooLarge();
                File.AppendAllText(FilePath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never be the reason the app crashes.
            }
        }
    }

    /// <summary>Keeps the log from growing forever — once it crosses the size cap, drops the
    /// older half rather than rotating to a second file, so there's always exactly one file to
    /// find and share.</summary>
    private static void TrimIfTooLarge()
    {
        if (!File.Exists(FilePath)) return;
        if (new FileInfo(FilePath).Length <= MaxBytes) return;

        var lines = File.ReadAllLines(FilePath);
        File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2));
    }
}
