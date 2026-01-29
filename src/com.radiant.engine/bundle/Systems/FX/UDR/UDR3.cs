using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class UDR3 : core.System
{
    // Temporal parameters
    public int FramesToAccumulate = 4;


    private int DebugEdges = 0;

    private RenderTarget2D SpatialTexture;
    private RenderTarget2D EdgeTexture;
    private RenderTarget2D TemporalTexture;
    private RenderTarget2D LastFrameTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private GizmosRenderer Gizmos;
    private Geometry Geometry;
    private KeyboardState PrevKeyState;

    private int FrameIndex = 0;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Geometry = Scene.ECS.GetSystem<Geometry>();

        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();
        PrevKeyState = Keyboard.GetState();
        FrameIndex = 0;
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
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

        EdgeTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);

        TemporalTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);

        LastFrameTexture = new RenderTarget2D(
            Renderer.Device,
            (int)OutputSize.X,
            (int)OutputSize.Y,
            false,
            SurfaceFormat.HalfVector4,
            DepthFormat.None);
    }

    private void ApplyRenderScale()
    {
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
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

        // Pass 1: Spatial (bilinear upsampling)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage1")
            .Configure(SamplerState.PointClamp)
            .SetTarget(SpatialTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("InputSize", inputSize)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Pass 2: Edge reconstruction (SDF + motion vector guided sharpening)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage2")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(EdgeTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", SpatialTexture)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("MotionVectorTexture", Geometry?.MotionVectorTexture)
            .SetParameter("AbsorptionTexture", Geometry?.AbsorptionTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)

            .SetParameter("DebugEdges", (float)DebugEdges)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Pass 3: Temporal accumulation
        // Weight for current frame: 1/N where N = min(FrameIndex+1, FramesToAccumulate)
        int effectiveFrames = Math.Min(FrameIndex + 1, FramesToAccumulate);
        float currentWeight = 1.0f / effectiveFrames;

        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage3")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(TemporalTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", EdgeTexture)
            .SetParameter("LastFrame", LastFrameTexture)
            .SetParameter("MotionVectorTexture", Geometry?.MotionVectorTexture)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("CurrentWeight", currentWeight)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Pass 4: Copy to LastFrame for next frame (unsharpened)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage4")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(LastFrameTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", TemporalTexture)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Pass 5: RCAS sharpening (reuse EdgeTexture for output)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage5")
            .Configure(SamplerState.PointClamp)
            .SetTarget(EdgeTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", TemporalTexture)
            .SetParameter("OutputSize", OutputSize)
            .Draw()
            .Commit()
            .SetTarget(null);

        if (FrameIndex < FramesToAccumulate)
            FrameIndex++;

        Gizmos?.Set("UDR3", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0}) [F4]");
        Gizmos?.Set("UDR3", $"Input Size: {input.Width}x{input.Height}");
        Gizmos?.Set("UDR3", $"Output Size: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("UDR3", $"Frames to Accumulate: {FramesToAccumulate} (current: {effectiveFrames})");
        string[] debugNames = { "OFF", "Edge Mask" };
        Gizmos?.Set("UDR3", $"Debug Edges: {debugNames[DebugEdges]} [K]");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            UDRQuality.Cycle();
        }

        if (key.IsKeyDown(Keys.K) && !PrevKeyState.IsKeyDown(Keys.K))
        {
            DebugEdges = (DebugEdges + 1) % 2; // 0=off, 1=edge mask
        }

        PrevKeyState = key;
    }

    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100)
            return;

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(EdgeTexture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    public RenderTarget2D GetOutput() => EdgeTexture;

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;
        if (OutputSize == newSize)
            return;

        DisposeRenderTargets();
        OutputSize = newSize;
        CreateRenderTargets();
        FrameIndex = 0;
    }

    private void DisposeRenderTargets()
    {
        SpatialTexture?.Dispose();
        EdgeTexture?.Dispose();
        TemporalTexture?.Dispose();
        LastFrameTexture?.Dispose();
    }

    public override void Dispose()
    {
        Renderer.RenderScale = 1.0f;

        DisposeRenderTargets();
        SpatialTexture = null;
        EdgeTexture = null;
        TemporalTexture = null;
        LastFrameTexture = null;
    }
}
