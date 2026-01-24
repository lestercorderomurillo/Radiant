using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class UDR2 : core.System
{
    private static readonly int[] ScaleFactors = { 25, 50, 100 };
    private static readonly string[] QualityNames = { "Performance", "Balanced", "Native" };
    private int QualityIndex = 1;
    private int ScaleFactor => ScaleFactors[QualityIndex];

    // Spatial parameters
    public float Sharpness = 0.6f;
    public bool EdgeCorrection = true;
    public bool DebugRays = false;

    // Temporal parameters (adjustable)
    public int FramesToAccumulate = 10;         // Number of frames to average together (1 = no temporal, 10 = very smooth)
    private const int MaxFrames = 16;

    private RenderTarget2D SpatialTexture;
    private RenderTarget2D AccumulationTexture;  // Running sum of frames
    private RenderTarget2D SmoothTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    private int FrameIndex = 0;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        PrevKeyState = Keyboard.GetState();
        FrameIndex = 0;

        if (Geometry != null)
            Geometry.EnableSDF = true;
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
        ResetAccumulation();
    }

    public void ResetAccumulation()
    {
        FrameIndex = 0;
        // Clear the accumulation buffer to avoid showing stale history
        if (AccumulationTexture != null)
        {
            Renderer.Device.SetRenderTarget(AccumulationTexture);
            Renderer.Device.Clear(Color.Black);
            Renderer.Device.SetRenderTarget(null);
        }
    }

    private void CreateRenderTargets()
    {
        SpatialTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);

        AccumulationTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);

        SmoothTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);
    }

    private float GetScaleFactorNormalized() => ScaleFactor / 100f;

    private void ApplyRenderScale()
    {
        Renderer.RenderScale = GetScaleFactorNormalized();
    }

    public void SetQuality(int index)
    {
        index = Math.Clamp(index, 0, ScaleFactors.Length - 1);

        if (QualityIndex != index)
        {
            QualityIndex = index;
            ApplyRenderScale();
        }
    }

    public override void Update()
    {
        if (InputSource == null)
            return;

        HandleInput();

        var input = InputSource();

        if (input == null)
            return;

        Vector2 inputSize = new Vector2(input.Width, input.Height);

        // Pass 1: Spatial upscaling (Lanczos + edge refinement)
        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Spatial")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SpatialTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .SetParameter("EdgeCorrection", EdgeCorrection ? 1f : 0f)
            .SetParameter("DebugRays", DebugRays ? 1f : 0f)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Pass 2: Temporal accumulation (running average)
        // Weight for current frame: 1/N where N = min(FrameIndex+1, FramesToAccumulate)
        int effectiveFrames = Math.Min(FrameIndex + 1, FramesToAccumulate);
        float currentWeight = 1.0f / effectiveFrames;

        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Temporal")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SmoothTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", SpatialTexture)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("HistoryTexture", AccumulationTexture)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("CurrentWeight", currentWeight)
            .SetParameter("FrameCount", (float)effectiveFrames)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Copy result to accumulation buffer for next frame
        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Copy")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(AccumulationTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", SmoothTexture)
            .Draw()
            .Commit()
            .SetTarget(null);

        FrameIndex++;

        Gizmos?.Set("UDR2", $"Quality: {QualityNames[QualityIndex]} ({GetScaleFactorNormalized():P0}) [F4]");
        Gizmos?.Set("UDR2", $"Input Size: {input.Width}x{input.Height}");
        Gizmos?.Set("UDR2", $"Output Size: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("UDR2", $"Sharpness: {Sharpness:F2} [F7/F8]");
        Gizmos?.Set("UDR2", $"Frames to Accumulate: {FramesToAccumulate} [T/Y] (current: {effectiveFrames})");
        Gizmos?.Set("UDR2", $"Detail Reconstruction: {(EdgeCorrection ? "On" : "Off")} [F9]");
        Gizmos?.Set("UDR2", $"Debug Rays: {(DebugRays ? "On" : "Off")} [F10]");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        // F4 to cycle quality
        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            QualityIndex = (QualityIndex + 1) % ScaleFactors.Length;
            ApplyRenderScale();
        }

        // T/Y to adjust frames to accumulate
        if (key.IsKeyDown(Keys.T) && !PrevKeyState.IsKeyDown(Keys.T))
        {
            FramesToAccumulate = Math.Max(1, FramesToAccumulate - 1);
            FrameIndex = 0;  // Reset accumulation
        }
        if (key.IsKeyDown(Keys.Y) && !PrevKeyState.IsKeyDown(Keys.Y))
        {
            FramesToAccumulate = Math.Min(MaxFrames, FramesToAccumulate + 1);
            FrameIndex = 0;  // Reset accumulation
        }

        // F7/F8 to adjust sharpness
        if (key.IsKeyDown(Keys.F7) && !PrevKeyState.IsKeyDown(Keys.F7))
        {
            Sharpness = Math.Max(0f, Sharpness - 0.1f);
        }
        if (key.IsKeyDown(Keys.F8) && !PrevKeyState.IsKeyDown(Keys.F8))
        {
            Sharpness = Math.Min(2f, Sharpness + 0.1f);
        }

        // F9 to toggle edge correction
        if (key.IsKeyDown(Keys.F9) && !PrevKeyState.IsKeyDown(Keys.F9))
        {
            EdgeCorrection = !EdgeCorrection;
        }

        // F10 to toggle debug rays visualization
        if (key.IsKeyDown(Keys.F10) && !PrevKeyState.IsKeyDown(Keys.F10))
        {
            DebugRays = !DebugRays;
        }

        PrevKeyState = key;
    }

    public override void Render()
    {
        if (InputSource == null || ScaleFactor == 100)
            return;

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(SmoothTexture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    public RenderTarget2D GetOutput() => SmoothTexture;

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;
        if (OutputSize == newSize)
            return;

        DisposeRenderTargets();
        OutputSize = newSize;
        CreateRenderTargets();
        FrameIndex = 0;  // Reset accumulation on resize
    }

    private void DisposeRenderTargets()
    {
        SpatialTexture?.Dispose();
        AccumulationTexture?.Dispose();
        SmoothTexture?.Dispose();
    }

    public override void Dispose()
    {
        // Restore render scale to 1.0 when UDR2 is disabled
        Renderer.RenderScale = 1.0f;

        DisposeRenderTargets();
        SpatialTexture = null;
        AccumulationTexture = null;
        SmoothTexture = null;
    }
}
