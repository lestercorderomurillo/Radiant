using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public class PerformanceMonitor : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.Overlay;
    // Dependencies
    private Process Process;
    private PerformanceCounter CpuCounter;
    private List<PerformanceCounter> GpuCounters;

    // Frame timing - zero-allocation circular buffer
    private const int FrameHistorySize = 64; // Power of 2 for fast modulo
    private const int FrameHistoryMask = FrameHistorySize - 1;
    private readonly float[] FrameTimeHistory = new float[FrameHistorySize];
    private int FrameHistoryIndex;
    private float FrameTimeSum;
    private float FrameTimeAverage;

    // FPS calculation
    private const float UpdateInterval = 1.00f;
    private float Fps;
    private int FrameCount;
    private float Elapsed;
    private float SmoothedDisplayFps; // Smoothed FPS for display (less flickery)

    // System metrics (volatile for cross-thread reads)
    private volatile float MemoryMB;
    private volatile float PeakMemoryMB;
    private volatile float CpuUsage;
    private int CpuCoreCount;
    private float CpuCoreCountInv;
    private volatile float GpuUsage;

    // Background sampling thread
    private Thread SamplerThread;
    private volatile bool SamplerRunning;
    private const int SampleIntervalMs = 500; // Sample every 500ms in background

    // Precomputed constants
    private const float BytesToMB = 1f / (1024f * 1024f);
    private const float FrameHistorySizeInv = 1f / FrameHistorySize;

    // Cached string builders - eliminates per-frame allocations
    private readonly StringBuilder FpsBuilder = new(32);
    private readonly StringBuilder FrameTimeBuilder = new(32);
    private readonly StringBuilder TargetBuilder = new(32);
    private readonly StringBuilder CpuBuilder = new(48);
    private readonly StringBuilder GpuBuilder = new(32);
    private readonly StringBuilder RamBuilder = new(32);
    private readonly StringBuilder PeakBuilder = new(32);

    public override void Initialize()
    {
        Process = Process.GetCurrentProcess();
        CpuCoreCount = Environment.ProcessorCount;
        CpuCoreCountInv = 1f / CpuCoreCount;

        InitializeCpuCounter();
        InitializeGpuCounters();
        StartSamplerThread();

        Inspector.CreateWindow("perf", "Metrics", 1);

        Inspector.AddSectionLabel("perf", "perfHeader", "Performance");
        Inspector.AddLabel("perf", "fps", "Frames Per Second: -");
        Inspector.AddLabel("perf", "frame", "Frame Time: -");
        Inspector.AddLabel("perf", "target", "Target: -");
        Inspector.AddLabel("perf", "cpu", "CPU: -");
        Inspector.AddLabel("perf", "gpu", "GPU: -");

        Inspector.AddSectionLabel("perf", "memHeader", "Memory");
        Inspector.AddLabel("perf", "ram", "RAM: -");
        Inspector.AddLabel("perf", "peak", "Peak: -");
    }

    private void StartSamplerThread()
    {
        SamplerRunning = true;
        SamplerThread = new Thread(SamplerLoop)
        {
            IsBackground = true,
            Name = "PerformanceMonitor Sampler",
            Priority = ThreadPriority.BelowNormal
        };
        SamplerThread.Start();
    }

    private void SamplerLoop()
    {
        while (SamplerRunning)
        {
            SampleSystemMetrics();
            Thread.Sleep(SampleIntervalMs);
        }
    }

    private void SampleSystemMetrics()
    {
        // CPU - runs in background, no main thread impact
        if (CpuCounter != null)
        {
            try
            {
                CpuUsage = CpuCounter.NextValue() * CpuCoreCountInv;
            }
            catch
            {
                CpuUsage = -1f;
            }
        }

        // GPU
        if (GpuCounters != null && GpuCounters.Count > 0)
        {
            try
            {
                float totalGpu = 0f;
                foreach (var counter in GpuCounters)
                    totalGpu += counter.NextValue();
                GpuUsage = totalGpu;
            }
            catch
            {
                GpuUsage = -1f;
            }
        }

        // Memory - Process.Refresh() is expensive, now runs in background
        try
        {
            Process.Refresh();
            MemoryMB = Process.WorkingSet64 * BytesToMB;
            PeakMemoryMB = Process.PeakWorkingSet64 * BytesToMB;
        }
        catch
        {
            // Process may be disposed
        }
    }

    private void InitializeCpuCounter()
    {
        try
        {
            CpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.ProcessName);
        }
        catch
        {
            CpuCounter = null;
        }
    }

    private void InitializeGpuCounters()
    {
        GpuCounters = new List<PerformanceCounter>();
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();

            foreach (var instance in instanceNames)
            {
                // Filter for 3D engine instances (engtype_3D)
                if (instance.Contains("engtype_3D"))
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                    GpuCounters.Add(counter);
                }
            }
        }
        catch
        {
            // GPU counters not available (older Windows or no GPU)
            GpuCounters.Clear();
        }
    }

    public override void Update()
    {
        float Delta = (float)GameTime.ElapsedGameTime.TotalSeconds;
        float DeltaMs = Delta * 1000f;

        UpdateFrameHistory(DeltaMs);

        FrameCount++;
        Elapsed += Delta;

        if (Elapsed >= UpdateInterval)
        {
            Fps = FrameCount / Elapsed;
            // System metrics now sampled in background thread - no syscalls here
            FrameCount = 0;
            Elapsed = 0f;
        }

        UpdateDisplay();
    }

    private void UpdateFrameHistory(float DeltaMs)
    {
        // Subtract old value, add new value - O(1) running average
        FrameTimeSum -= FrameTimeHistory[FrameHistoryIndex];
        FrameTimeHistory[FrameHistoryIndex] = DeltaMs;
        FrameTimeSum += DeltaMs;
        
        FrameHistoryIndex = (FrameHistoryIndex + 1) & FrameHistoryMask;
        FrameTimeAverage = FrameTimeSum * FrameHistorySizeInv;
    }

    private void UpdateDisplay()
    {
        var gameLoop = Renderer.GameLoop;
        int TargetFps = gameLoop?.TargetFramesPerSecond ?? 144;
        float ActualFps = gameLoop?.FramesPerSecond ?? 1f;
        bool Uncapped = TargetFps <= 0;

        // Smooth FPS display - exponential moving average (less flickery)
        SmoothedDisplayFps = SmoothedDisplayFps == 0 ? ActualFps : SmoothedDisplayFps * 0.9f + ActualFps * 0.1f;
        int DisplayFps = (int)MathF.Round(SmoothedDisplayFps);

        FpsBuilder.Clear().Append("Frames Per Second: ").Append(DisplayFps);
        if (!Uncapped) FpsBuilder.Append('/').Append(TargetFps);
        Inspector.SetLabel("perf", "fps", FpsBuilder.ToString());

        FrameTimeBuilder.Clear().Append("Frame Time: ").AppendFormat("{0:F2}", FrameTimeAverage).Append("ms");
        Inspector.SetLabel("perf", "frame", FrameTimeBuilder.ToString());

        if (!Uncapped)
        {
            TargetBuilder.Clear().Append("Target: ").AppendFormat("{0:F2}", 1000f / TargetFps).Append("ms (").Append(TargetFps).Append(" fps)");
            Inspector.SetLabel("perf", "target", TargetBuilder.ToString());
        }
        else
        {
            Inspector.SetLabel("perf", "target", "Target: Uncapped");
        }

        CpuBuilder.Clear().Append("CPU: ").AppendFormat("{0:F1}", CpuUsage).Append("% (").Append(CpuCoreCount).Append(" cores)");
        Inspector.SetLabel("perf", "cpu", CpuBuilder.ToString());

        GpuBuilder.Clear().Append("GPU: ").AppendFormat("{0:F1}", GpuUsage).Append('%');
        Inspector.SetLabel("perf", "gpu", GpuBuilder.ToString());

        RamBuilder.Clear().Append("RAM: ").AppendFormat("{0:F1}", MemoryMB).Append("MB");
        Inspector.SetLabel("perf", "ram", RamBuilder.ToString());

        PeakBuilder.Clear().Append("Peak: ").AppendFormat("{0:F1}", PeakMemoryMB).Append("MB");
        Inspector.SetLabel("perf", "peak", PeakBuilder.ToString());
    }

    #region Public API
    public float GetFps() => Fps;
    public float GetFrameTimeAverage() => FrameTimeAverage;
    public float GetMemoryMB() => MemoryMB;
    public float GetCpuUsage() => CpuUsage;
    public float GetGpuUsage() => GpuUsage;
    #endregion

    public override void Render() { }

    public override void Dispose()
    {
        // Stop sampler thread first
        SamplerRunning = false;
        SamplerThread?.Join(500);

        CpuCounter?.Dispose();
        if (GpuCounters != null)
        {
            foreach (var counter in GpuCounters)
                counter.Dispose();
            GpuCounters.Clear();
        }
        Process?.Dispose();
    }
}