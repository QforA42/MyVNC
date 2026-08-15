using System.Diagnostics;

namespace MyVNC.App.Services;

/// <summary>
/// Watches this process's own memory/CPU usage on its own dedicated background thread —
/// independent of the WPF dispatcher, so it keeps sampling even if the UI thread itself were the
/// one stuck — and warns + logs if MyVNC looks like it's gone amok. This is exactly the failure
/// mode a runaway named-pipe retry loop produced earlier in this project's history (multiple GB
/// of memory growth within seconds); this exists so it's caught and evidenced immediately
/// instead of relying on someone noticing Task Manager.
/// </summary>
public static class ResourceWatchdog
{
    // Normal operation observed in practice is ~150-450MB; real runaway incidents hit 2-7GB.
    // 1.5GB sits well above the former and well below the latter.
    private const long MemoryWarningBytes = 1_500_000_000;

    // "Core-equivalents" = CPU time consumed / wall-clock time elapsed, so this is meaningful
    // regardless of how many logical cores the machine has (a single busy-looping thread pegs
    // one core, which is a tiny percentage of "all cores" on a many-core machine but is still
    // 100% abnormal for an idle GUI app).
    private const double CoreEquivalentWarningThreshold = 0.5;

    private const int ConsecutiveSamplesToWarn = 3;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    public static void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "MyVNC-ResourceWatchdog" };
        thread.Start();
    }

    private static void Run()
    {
        var process = Process.GetCurrentProcess();
        var lastCpuTime = process.TotalProcessorTime;
        var lastSampleAt = DateTime.UtcNow;
        var consecutiveHighSamples = 0;
        var alreadyWarned = false;

        while (true)
        {
            Thread.Sleep(SampleInterval);
            try
            {
                process.Refresh();
                var now = DateTime.UtcNow;
                var cpuTime = process.TotalProcessorTime;
                var coreEquivalents = (cpuTime - lastCpuTime).TotalSeconds / Math.Max(0.001, (now - lastSampleAt).TotalSeconds);
                var memoryBytes = process.WorkingSet64;
                lastCpuTime = cpuTime;
                lastSampleAt = now;

                var isHigh = memoryBytes > MemoryWarningBytes || coreEquivalents > CoreEquivalentWarningThreshold;
                consecutiveHighSamples = isHigh ? consecutiveHighSamples + 1 : 0;

                if (consecutiveHighSamples >= ConsecutiveSamplesToWarn && !alreadyWarned)
                {
                    alreadyWarned = true;
                    Warn(memoryBytes, coreEquivalents);
                }
                else if (!isHigh)
                {
                    alreadyWarned = false; // usage recovered — allow warning again if it recurs later
                }
            }
            catch
            {
                // Best-effort monitoring; the watchdog must never itself crash the app.
            }
        }
    }

    // No longer shows a MessageBox: live use showed this threshold trips routinely during
    // ordinary active sessions (framebuffer decode/render load from clicking around), not just
    // genuine runaway incidents — a popup on every busy moment was more noise than signal. Still
    // always logged (regardless of the debug-logging toggle) so a real runaway is still evidenced
    // in myvnc.log without interrupting the user mid-session.
    private static void Warn(long memoryBytes, double coreEquivalents)
    {
        var memoryMb = (int)(memoryBytes / 1024.0 / 1024.0);
        var cpuPercent = (int)(coreEquivalents * 100);

        AppLog.WriteAlways($"RESOURCE WARNING: memory={memoryMb}MB, cpu~{cpuPercent}% of one core (sustained)");
    }
}
