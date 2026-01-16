// GameLoop.cs - Optimized frame pacing version
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace com.radiant.engine.runtime;

public class GameLoop : GameObject
{
    private const int NO_SCENE = -1;

    private Scene[] Scenes = [];

    private int SceneId = NO_SCENE;

    private int NextSceneId = NO_SCENE;

    private Window Window;

    public SpriteBatch SpriteBatch { get; set; }

    public GameTime GameTime { get; set; }

    // Timing configuration
    public int TargetFramesPerSecond { get; private set; } = 144;

    public int TargetUpdatesPerSecond { get; private set; } = 64;

    private double UpdateInterval;

    private double FrameInterval;

    // Timing state
    private Stopwatch GlobalTimer = new();

    private long LastUpdateTicks;

    private long LastFrameTicks;

    private double FixedUpdateAccumulator;

    // FPS tracking
    public float FramesPerSecond { get; private set; }

    private int FrameCount;

    private double LastFpsUpdate;

    public void Initialize(Window window)
    {
        Window = window;
        Initialize();
    }

    public override void Initialize()
    {
        UpdateInterval = 1.0 / TargetUpdatesPerSecond;
        FrameInterval = 1.0 / TargetFramesPerSecond;

        GlobalTimer.Start();
        LastUpdateTicks = GlobalTimer.ElapsedTicks;
        LastFrameTicks = GlobalTimer.ElapsedTicks;
        LastFpsUpdate = GlobalTimer.Elapsed.TotalSeconds;
    }

    public override void Dispose()
    {
    }

    public void AddScene(Scene scene) => Scenes = [.. Scenes, scene];

    private void TransitionScene(int id)
    {
        if (SceneId != NO_SCENE)
            Scenes[SceneId].Dispose();

        SceneId = id;

        if (SceneId != NO_SCENE)
        {
            Scenes[SceneId].RenderPipeline = new RenderPipeline(Window);
            Scenes[SceneId].Initialize();
            Scenes[SceneId].SetupECS();
            Scenes[SceneId].SetupScene();
        }

        NextSceneId = NO_SCENE;
    }

    public void SetActiveSceneId(int id) => NextSceneId = id;

    public override void Update()
    {
        // Auto-start first scene if no active scene
        if (SceneId == NO_SCENE && Scenes.Length > 0)
        {
            TransitionScene(0);
        }

        // Calculate delta time using high-precision timer
        long currentTicks = GlobalTimer.ElapsedTicks;
        double deltaTime = (currentTicks - LastUpdateTicks) / (double)Stopwatch.Frequency;

        LastUpdateTicks = currentTicks;

        // Process fixed updates
        FixedUpdateAccumulator += deltaTime;

        while (FixedUpdateAccumulator >= UpdateInterval)
        {
            if (SceneId != NO_SCENE)
            {
                Scenes[SceneId].GameTime = GameTime;
                Scenes[SceneId].DeltaTime = (float)(1.0f / TargetUpdatesPerSecond);
                Scenes[SceneId].FixedUpdate();
            }

            FixedUpdateAccumulator -= UpdateInterval;
        }

        // Variable update with precise delta time
        if (SceneId != NO_SCENE)
            Scenes[SceneId].GameTime = GameTime;
        Scenes[SceneId].DeltaTime = (float)deltaTime;
        Scenes[SceneId].Update();

        if (NextSceneId != NO_SCENE && NextSceneId != SceneId)
            TransitionScene(NextSceneId);
    }

    public override void Render()
    {
        // Frame pacing with hybrid sleep/spin-wait
        if (TargetFramesPerSecond > 0)
        {
            long currentTicks = GlobalTimer.ElapsedTicks;
            long targetTicks = LastFrameTicks + (long)(FrameInterval * Stopwatch.Frequency);

            if (currentTicks < targetTicks)
            {
                long remainingTicks = targetTicks - currentTicks;
                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

                int sleepMs = (int)(remainingMs - 1);
                if (sleepMs > 0) Thread.Sleep(sleepMs);

                while (GlobalTimer.ElapsedTicks < targetTicks) { }

                LastFrameTicks = targetTicks;
            }
            else
            {
                // We're behind - reset to NOW, don't try to catch up
                LastFrameTicks = currentTicks;
            }
        }
        else
        {
            LastFrameTicks = GlobalTimer.ElapsedTicks;
        }

        // FPS calculation
        FrameCount++;

        double now = GlobalTimer.Elapsed.TotalSeconds;

        if (now - LastFpsUpdate >= 1.0)
        {
            FramesPerSecond = (float)(FrameCount / (now - LastFpsUpdate));
            FrameCount = 0;
            LastFpsUpdate = now;
        }

        // Actual rendering
        if (SceneId != NO_SCENE)
        {
            Scenes[SceneId].RenderPipeline.Window.GraphicsDevice.Clear(Color.Black);
            Scenes[SceneId].GameTime = GameTime;
            Scenes[SceneId].Render();
            Scenes[SceneId].LateRender();
        }
    }
}