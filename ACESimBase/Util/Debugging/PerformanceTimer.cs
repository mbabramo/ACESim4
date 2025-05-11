namespace ACESimBase.Util.Debugging;

using System;
using System.Diagnostics;

public sealed class PerformanceTimer
{
    private readonly Stopwatch _stopwatch = new();
    private long _startMemoryBytes;

    /// <summary>
    /// Captures baseline managed-heap usage and starts the timer.
    /// </summary>
    public void Start()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        _startMemoryBytes = GC.GetTotalMemory(true);
        _stopwatch.Restart();
    }

    /// <summary>
    /// Stops the timer and returns elapsed time plus the managed-heap delta in the most readable unit.
    /// </summary>
    public string End()
    {
        _stopwatch.Stop();

        long endMemoryBytes = GC.GetTotalMemory(false);
        long memoryDeltaBytes = endMemoryBytes - _startMemoryBytes;

        string formattedElapsed = $"{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}";
        string formattedMemory = FormatBytes(memoryDeltaBytes);

        return $"{formattedElapsed} | Memory: {formattedMemory}";
    }

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        return bytes switch
        {
            >= (long) GB => $"{bytes / GB:F2} GB ({bytes:N0} bytes)",
            >= (long) MB => $"{bytes / MB:F2} MB ({bytes:N0} bytes)",
            >= (long) KB => $"{bytes / KB:F2} KB ({bytes:N0} bytes)",
            _ => $"{bytes:N0} bytes"
        };
    }
}


