using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.runtime;

public partial class GameLoop : IGameObject
{
    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);

    private const int NoScene = -1;

    private Scene[] Scenes = [];

    private int SceneId = NoScene;

    private int NextSceneId = NoScene;

    private Window Window;

    public SpriteBatch SpriteBatch { get; set; }

    public GameTime GameTime { get; set; }

    // Timing configuration
    public static readonly int[] FpsOptions = [60, 120, 144, 0];
    public static readonly string[] FpsOptionNames = ["60", "120", "144", "No Limit"];
    public int TargetFramesPerSecond { get; private set; } = 144;
    public const int UnfocusedFramesPerSecond = 30;
    public bool ThrottleUnfocused = true;

    public int TargetUpdatesPerSecond { get; private set; } = 64;

    private double UpdateInterval;

    private double FrameInterval;
    private static readonly double UnfocusedFrameInterval = 1.0 / UnfocusedFramesPerSecond;

    // Timing state
    private Stopwatch GlobalTimer = new();

    private long LastUpdateTicks;

    private long LastFrameTicks;
    private long LastFrameTimingTicks;

    private double FixedUpdateAccumulator;

    // Maximum fixed updates per frame to prevent spiral of death
    private const int MaxFixedUpdatesPerFrame = 8;

    // FPS tracking
    public float FramesPerSecond { get; private set; }
    public float FrameTimeMs { get; private set; }
    public float FrameTimeSmoothed { get; private set; }
    public float RenderTimeMs { get; private set; }
    public float RenderTimeSmoothed { get; private set; }

    private int FrameCount;

    private double LastFpsUpdate;
    private const float FrameTimeSmoothing = 0.9f; // EMA smoothing factor

    public void Initialize(Window window)
    {
        Window = window;
        Initialize();
    }

    public void Initialize()
    {
        UpdateInterval = 1.0 / TargetUpdatesPerSecond;
        FrameInterval = TargetFramesPerSecond > 0 ? 1.0 / TargetFramesPerSecond : 0;

        timeBeginPeriod(1);
        GlobalTimer.Start();
        LastUpdateTicks = GlobalTimer.ElapsedTicks;
        LastFrameTicks = GlobalTimer.ElapsedTicks;
        LastFrameTimingTicks = GlobalTimer.ElapsedTicks;
        LastFpsUpdate = GlobalTimer.Elapsed.TotalSeconds;
    }

    public void SetTargetFps(int fps)
    {
        TargetFramesPerSecond = fps;
        FrameInterval = fps > 0 ? 1.0 / fps : 0;
    }

    public void Dispose()
    {
        timeEndPeriod(1);
        GC.SuppressFinalize(this);
    }

    public void AddScene(Scene scene) => Scenes = [.. Scenes, scene];

    private void TransitionScene(int id)
    {
        if (SceneId != NoScene)
            Scenes[SceneId].Dispose();

        SceneId = id;

        if (SceneId != NoScene)
        {
            Scenes[SceneId].Renderer = new Renderer(Window);
            Scenes[SceneId].Initialize();
            Scenes[SceneId].SetupECS();
            Scenes[SceneId].SetupScene();
        }

        NextSceneId = NoScene;
    }

    public void SetActiveSceneId(int id) => NextSceneId = id;

    public void Update()
    {
        // Auto-start first scene if no active scene
        if (SceneId == NoScene && Scenes.Length > 0)
        {
            TransitionScene(0);
        }

        // Calculate delta time using high-precision timer
        long currentTicks = GlobalTimer.ElapsedTicks;
        double deltaTime = (currentTicks - LastUpdateTicks) / (double)Stopwatch.Frequency;

        LastUpdateTicks = currentTicks;

        // Process fixed updates with cap to prevent spiral of death
        FixedUpdateAccumulator += deltaTime;

        int iterations = 0;
        while (FixedUpdateAccumulator >= UpdateInterval && iterations++ < MaxFixedUpdatesPerFrame)
        {
            if (SceneId != NoScene)
            {
                Scenes[SceneId].GameTime = GameTime;
                Scenes[SceneId].DeltaTime = (float)(1.0 / TargetUpdatesPerSecond);
                Scenes[SceneId].InternalFixedUpdate();
            }

            FixedUpdateAccumulator -= UpdateInterval;
        }

        // Discard excess accumulated time to prevent catch-up attempts
        if (FixedUpdateAccumulator > UpdateInterval)
            FixedUpdateAccumulator = UpdateInterval;

        // Variable update with precise delta time
        if (SceneId != NoScene)
        {
            Scenes[SceneId].GameTime = GameTime;
            Scenes[SceneId].DeltaTime = (float)deltaTime;
            Scenes[SceneId].InternalUpdate();
        }

        if (NextSceneId != NoScene && NextSceneId != SceneId)
            TransitionScene(NextSceneId);
    }

    public void Render()
    {
        long frameStartTicks = GlobalTimer.ElapsedTicks;

        // Frame pacing at START — waits for ideal target, accounts for all inter-frame overhead
        bool focused = Window.IsActive;
        double activeInterval = (!focused && ThrottleUnfocused) ? UnfocusedFrameInterval : FrameInterval;
        if (activeInterval > 0)
        {
            long targetTicks = LastFrameTicks + (long)(activeInterval * Stopwatch.Frequency);

            if (frameStartTicks < targetTicks)
            {
                double remainingMs = (targetTicks - frameStartTicks) * 1000.0 / Stopwatch.Frequency;

                // Yield in small chunks instead of sleeping (avoids 15ms scheduler quantum)
                while (remainingMs > 2.0)
                {
                    Thread.Sleep(1);
                    remainingMs = (targetTicks - GlobalTimer.ElapsedTicks) * 1000.0 / Stopwatch.Frequency;
                }

                // Short spin-wait for precision (< 2ms)
                while (GlobalTimer.ElapsedTicks < targetTicks)
                    Thread.SpinWait(10);

                // Anchor to ideal target — prevents drift from overshoot accumulation
                LastFrameTicks = targetTicks;
            }
            else
            {
                // Behind schedule — reset to NOW, don't try to catch up
                LastFrameTicks = frameStartTicks;
            }
        }
        else
        {
            LastFrameTicks = frameStartTicks;
        }

        // Frame-to-frame timing (measured before pacing wait, includes all overhead)
        FrameTimeMs = (float)((frameStartTicks - LastFrameTimingTicks) * 1000.0 / Stopwatch.Frequency);
        FrameTimeSmoothed = FrameTimeSmoothed * FrameTimeSmoothing + FrameTimeMs * (1f - FrameTimeSmoothing);
        LastFrameTimingTicks = frameStartTicks;

        // FPS calculation (0.5s window)
        FrameCount++;
        double now = GlobalTimer.Elapsed.TotalSeconds;
        if (now - LastFpsUpdate >= 0.5)
        {
            FramesPerSecond = (float)(FrameCount / (now - LastFpsUpdate));
            FrameCount = 0;
            LastFpsUpdate = now;
        }

        // Actual rendering
        long renderStartTicks = GlobalTimer.ElapsedTicks;
        if (SceneId != NoScene)
        {
            Scenes[SceneId].Renderer.ClearBackBuffer(Color.Black);
            Scenes[SceneId].GameTime = GameTime;
            Scenes[SceneId].InternalRender();
            Scenes[SceneId].InternalLateRender();
            Scenes[SceneId].Renderer.PresentToBackBuffer();
        }

        long renderEndTicks = GlobalTimer.ElapsedTicks;
        RenderTimeMs = (float)((renderEndTicks - renderStartTicks) * 1000.0 / Stopwatch.Frequency);
        RenderTimeSmoothed = RenderTimeSmoothed * FrameTimeSmoothing + RenderTimeMs * (1f - FrameTimeSmoothing);
    }

    public void FixedUpdate() { }
    public void LateRender() { }
}