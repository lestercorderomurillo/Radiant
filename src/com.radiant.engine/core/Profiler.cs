using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace com.radiant.engine.core;

/// <summary>
/// High-precision CPU profiler for measuring execution time across the engine.
/// Thread-safe and designed for minimal overhead.
/// </summary>
public static class Profiler
{
    private static readonly ConcurrentDictionary<string, ProfilerSection> Sections = new();
    private static readonly Stopwatch GlobalTimer = Stopwatch.StartNew();
    private static readonly ThreadLocal<Stack<(string Name, long StartTicks)>> SectionStack = new(() => new());
    private static bool _enabled = true;
    private static long _sessionStartTicks;
    private static int _totalFrames;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    static Profiler()
    {
        _sessionStartTicks = GlobalTimer.ElapsedTicks;
    }

    /// <summary>
    /// Begins profiling a named section. Must be paired with EndSection().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginSection(string name)
    {
        if (!_enabled) return;
        SectionStack.Value!.Push((name, GlobalTimer.ElapsedTicks));
    }

    /// <summary>
    /// Ends the most recently started section and records the timing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EndSection()
    {
        if (!_enabled) return;

        var stack = SectionStack.Value!;
        if (stack.Count == 0) return;

        var (name, startTicks) = stack.Pop();
        long elapsed = GlobalTimer.ElapsedTicks - startTicks;

        var section = Sections.GetOrAdd(name, _ => new ProfilerSection(name));
        section.Record(elapsed);
    }

    /// <summary>
    /// Profiles a section using a disposable scope. Usage: using (Profiler.Section("Name")) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProfilerScope Section(string name)
    {
        return new ProfilerScope(name, _enabled);
    }

    /// <summary>
    /// Records timing for a section directly. Used internally by ProfilerScope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void RecordSection(string name, long ticks)
    {
        var section = Sections.GetOrAdd(name, _ => new ProfilerSection(name));
        section.Record(ticks);
    }

    /// <summary>
    /// Call once per frame to track frame count.
    /// </summary>
    public static void MarkFrame()
    {
        if (_enabled)
            Interlocked.Increment(ref _totalFrames);
    }

    /// <summary>
    /// Resets all profiling data.
    /// </summary>
    public static void Reset()
    {
        Sections.Clear();
        _sessionStartTicks = GlobalTimer.ElapsedTicks;
        _totalFrames = 0;
    }

    /// <summary>
    /// Gets the total session time in seconds.
    /// </summary>
    public static double GetSessionTimeSeconds()
    {
        return (GlobalTimer.ElapsedTicks - _sessionStartTicks) / (double)Stopwatch.Frequency;
    }

    /// <summary>
    /// Writes profiling results to a file and returns the path.
    /// </summary>
    public static string WriteResultsToFile(string directory = null)
    {
        directory ??= Environment.CurrentDirectory;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filePath = Path.Combine(directory, $"profiler_results_{timestamp}.txt");

        File.WriteAllText(filePath, GenerateReport());
        return filePath;
    }

    /// <summary>
    /// Generates a formatted profiling report.
    /// </summary>
    public static string GenerateReport()
    {
        var sb = new StringBuilder();
        double sessionTime = GetSessionTimeSeconds();
        int frameCount = _totalFrames;
        double avgFps = frameCount > 0 ? frameCount / sessionTime : 0;

        sb.AppendLine("================================================================================");
        sb.AppendLine("                         RADIANT ENGINE PROFILER REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine($"Session Duration: {sessionTime:F2} seconds");
        sb.AppendLine($"Total Frames: {frameCount}");
        sb.AppendLine($"Average FPS: {avgFps:F1}");
        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                              SECTION BREAKDOWN");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // Sort by total time descending
        var sortedSections = Sections.Values
            .OrderByDescending(s => s.TotalTicks)
            .ToList();

        if (sortedSections.Count == 0)
        {
            sb.AppendLine("No profiling data collected.");
            return sb.ToString();
        }

        // Calculate total profiled time for percentage
        long totalProfiledTicks = sortedSections.Sum(s => s.TotalTicks);

        // Header
        sb.AppendLine($"{"Section",-45} {"Calls",10} {"Total(ms)",12} {"Avg(ms)",10} {"Min(ms)",10} {"Max(ms)",10} {"% Time",8}");
        sb.AppendLine(new string('-', 110));

        foreach (var section in sortedSections)
        {
            double totalMs = section.TotalTicks * 1000.0 / Stopwatch.Frequency;
            double avgMs = section.CallCount > 0 ? totalMs / section.CallCount : 0;
            double minMs = section.MinTicks * 1000.0 / Stopwatch.Frequency;
            double maxMs = section.MaxTicks * 1000.0 / Stopwatch.Frequency;
            double percent = totalProfiledTicks > 0 ? (section.TotalTicks * 100.0 / totalProfiledTicks) : 0;

            sb.AppendLine($"{section.Name,-45} {section.CallCount,10} {totalMs,12:F3} {avgMs,10:F4} {minMs,10:F4} {maxMs,10:F3} {percent,7:F1}%");
        }

        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                           PERFORMANCE ANALYSIS");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // Identify bottlenecks
        var topSections = sortedSections.Take(5).ToList();
        if (topSections.Count > 0)
        {
            sb.AppendLine("TOP 5 CPU CONSUMERS (by total time):");
            sb.AppendLine();
            for (int i = 0; i < topSections.Count; i++)
            {
                var section = topSections[i];
                double totalMs = section.TotalTicks * 1000.0 / Stopwatch.Frequency;
                double percent = totalProfiledTicks > 0 ? (section.TotalTicks * 100.0 / totalProfiledTicks) : 0;
                sb.AppendLine($"  {i + 1}. {section.Name} - {totalMs:F1}ms total ({percent:F1}%)");
            }
            sb.AppendLine();
        }

        // Find high-variance sections (potential spikes)
        var highVarianceSections = sortedSections
            .Where(s => s.CallCount > 10 && s.MaxTicks > s.MinTicks * 5)
            .OrderByDescending(s => (double)s.MaxTicks / Math.Max(s.MinTicks, 1))
            .Take(5)
            .ToList();

        if (highVarianceSections.Count > 0)
        {
            sb.AppendLine("HIGH VARIANCE SECTIONS (potential frame spikes):");
            sb.AppendLine();
            foreach (var section in highVarianceSections)
            {
                double minMs = section.MinTicks * 1000.0 / Stopwatch.Frequency;
                double maxMs = section.MaxTicks * 1000.0 / Stopwatch.Frequency;
                double ratio = minMs > 0 ? maxMs / minMs : maxMs;
                sb.AppendLine($"  - {section.Name}: {minMs:F4}ms to {maxMs:F3}ms (ratio: {ratio:F1}x)");
            }
            sb.AppendLine();
        }

        // Per-frame analysis
        if (frameCount > 0)
        {
            sb.AppendLine("PER-FRAME BREAKDOWN (average per frame):");
            sb.AppendLine();

            var perFrameSections = sortedSections
                .Where(s => s.CallCount >= frameCount * 0.5) // Called at least half the frames
                .OrderByDescending(s => s.TotalTicks / (double)frameCount)
                .Take(10)
                .ToList();

            foreach (var section in perFrameSections)
            {
                double msPerFrame = (section.TotalTicks * 1000.0 / Stopwatch.Frequency) / frameCount;
                sb.AppendLine($"  - {section.Name}: {msPerFrame:F4}ms/frame");
            }
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("================================================================================");
        sb.AppendLine("                            RECOMMENDATIONS");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        if (topSections.Count > 0)
        {
            var top = topSections[0];
            double topPercent = totalProfiledTicks > 0 ? (top.TotalTicks * 100.0 / totalProfiledTicks) : 0;

            if (topPercent > 50)
            {
                sb.AppendLine($"CRITICAL: '{top.Name}' consumes {topPercent:F0}% of CPU time.");
                sb.AppendLine("          This is likely your primary bottleneck.");
            }
            else if (topPercent > 30)
            {
                sb.AppendLine($"WARNING: '{top.Name}' consumes {topPercent:F0}% of CPU time.");
                sb.AppendLine("         Consider optimizing this section.");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Generated by Radiant Engine Profiler");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    /// <summary>
    /// Gets a snapshot of all sections for real-time display.
    /// </summary>
    public static IReadOnlyList<ProfilerSectionSnapshot> GetSectionSnapshots()
    {
        return Sections.Values
            .Select(s => new ProfilerSectionSnapshot(
                s.Name,
                s.CallCount,
                s.TotalTicks * 1000.0 / Stopwatch.Frequency,
                s.MinTicks * 1000.0 / Stopwatch.Frequency,
                s.MaxTicks * 1000.0 / Stopwatch.Frequency))
            .OrderByDescending(s => s.TotalMs)
            .ToList();
    }
}

/// <summary>
/// Thread-safe container for profiling statistics of a single section.
/// </summary>
public class ProfilerSection
{
    public string Name { get; }
    private long _totalTicks;
    private long _minTicks = long.MaxValue;
    private long _maxTicks;
    private int _callCount;

    public long TotalTicks => Interlocked.Read(ref _totalTicks);
    public long MinTicks => Interlocked.Read(ref _minTicks);
    public long MaxTicks => Interlocked.Read(ref _maxTicks);
    public int CallCount => _callCount;

    public ProfilerSection(string name)
    {
        Name = name;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long ticks)
    {
        Interlocked.Add(ref _totalTicks, ticks);
        Interlocked.Increment(ref _callCount);

        // Update min/max with compare-exchange pattern
        long currentMin = _minTicks;
        while (ticks < currentMin)
        {
            long prev = Interlocked.CompareExchange(ref _minTicks, ticks, currentMin);
            if (prev == currentMin) break;
            currentMin = prev;
        }

        long currentMax = _maxTicks;
        while (ticks > currentMax)
        {
            long prev = Interlocked.CompareExchange(ref _maxTicks, ticks, currentMax);
            if (prev == currentMax) break;
            currentMax = prev;
        }
    }
}

/// <summary>
/// Disposable scope for automatic section timing.
/// </summary>
public readonly struct ProfilerScope : IDisposable
{
    private readonly string _name;
    private readonly long _startTicks;
    private readonly bool _enabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ProfilerScope(string name, bool enabled)
    {
        _name = name;
        _enabled = enabled;
        _startTicks = enabled ? Stopwatch.GetTimestamp() : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (!_enabled) return;
        long elapsed = Stopwatch.GetTimestamp() - _startTicks;
        Profiler.RecordSection(_name, elapsed);
    }
}

/// <summary>
/// Immutable snapshot of section statistics for display.
/// </summary>
public readonly record struct ProfilerSectionSnapshot(
    string Name,
    int CallCount,
    double TotalMs,
    double MinMs,
    double MaxMs)
{
    public double AvgMs => CallCount > 0 ? TotalMs / CallCount : 0;
}
