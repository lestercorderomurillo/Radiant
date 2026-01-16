using System;
using System.Text;
using System.Diagnostics;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public class PerformanceMonitor : core.System
{
    // Dependencies
    private GizmosRenderer Gizmos;
    private Process Process;
    private PerformanceCounter CpuCounter;

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

    // System metrics
    private float MemoryMB;
    private float PeakMemoryMB;
    private float CpuUsage;
    private int CpuCoreCount;
    private float CpuCoreCountInv;

    // Precomputed constants
    private const float BytesToMB = 1f / (1024f * 1024f);
    private const float FrameHistorySizeInv = 1f / FrameHistorySize;

    // Cached string builders - eliminates per-frame allocations
    private readonly StringBuilder FpsBuilder = new(32);
    private readonly StringBuilder FrameTimeBuilder = new(32);
    private readonly StringBuilder CpuBuilder = new(48);
    private readonly StringBuilder RamBuilder = new(32);
    private readonly StringBuilder PeakBuilder = new(32);

    public override void Initialize()
    {
        base.Initialize();

        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Process = Process.GetCurrentProcess();
        CpuCoreCount = Environment.ProcessorCount;
        CpuCoreCountInv = 1f / CpuCoreCount;

        InitializeCpuCounter();
        InitializeSections();
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

    private void InitializeSections()
    {
        Gizmos.AddSection("Performance", "Performance", Color.DarkKhaki);
        Gizmos.AddSection("Memory", "Memory", Color.Gold);
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
            
            UpdateSystemMetrics();
            
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

    private void UpdateSystemMetrics()
    {
        // CPU
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

        // Memory
        Process.Refresh();
        MemoryMB = Process.WorkingSet64 * BytesToMB;
        PeakMemoryMB = Process.PeakWorkingSet64 * BytesToMB;
    }

    private void UpdateDisplay()
    {
        int TargetFps = RenderPipeline.Window.GameLoop?.TargetFramesPerSecond ?? 144;
        float ActualFps = RenderPipeline.Window.GameLoop?.FramesPerSecond ?? 1f;
        int ActualFpsRounded = (int)MathF.Round(ActualFps);

        Gizmos.ClearSection("Performance");
        
        // Reuse StringBuilders - zero allocation
        FpsBuilder.Clear().Append("FPS: ").Append(ActualFpsRounded).Append('/').Append(TargetFps);
        Gizmos.AddSectionString("Performance", FpsBuilder.ToString());

        FrameTimeBuilder.Clear().Append("Frametime: ").AppendFormat("{0:F1}", FrameTimeAverage).Append("ms");
        Gizmos.AddSectionString("Performance", FrameTimeBuilder.ToString());

        CpuBuilder.Clear().Append("CPU: ").AppendFormat("{0:F1}", CpuUsage).Append("% (").Append(CpuCoreCount).Append(" cores)");
        Gizmos.AddSectionString("Performance", CpuBuilder.ToString());

        Gizmos.ClearSection("Memory");
        
        RamBuilder.Clear().Append("RAM: ").AppendFormat("{0:F1}", MemoryMB).Append("MB");
        Gizmos.AddSectionString("Memory", RamBuilder.ToString());

        PeakBuilder.Clear().Append("Peak: ").AppendFormat("{0:F1}", PeakMemoryMB).Append("MB");
        Gizmos.AddSectionString("Memory", PeakBuilder.ToString());
    }

    #region Public API
    public float GetFps() => Fps;
    public float GetFrameTimeAverage() => FrameTimeAverage;
    public float GetMemoryMB() => MemoryMB;
    public float GetCpuUsage() => CpuUsage;
    #endregion

    public override void Render() { }

    public override void Dispose()
    {
        CpuCounter?.Dispose();
        Process?.Dispose();
    }
}