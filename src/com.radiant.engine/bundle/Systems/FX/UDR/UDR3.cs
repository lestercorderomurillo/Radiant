using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class UDR3 : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    // Temporal parameters
    public int FramesToAccumulate = 4;


    private int DebugEdges = 0;

    private RenderTarget2D SpatialTexture;
    private RenderTarget2D EdgeTexture;
    private RenderTarget2D TemporalTexture;
    private RenderTarget2D LastFrameTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;

    private int FrameIndex = 0;

    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();

        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();
        FrameIndex = 0;

        Inspector.CreateWindow("udr3", "UDR3");
        Inspector.AddLabel("udr3", "quality", "...");
        Inspector.AddLabel("udr3", "input", "...");
        Inspector.AddLabel("udr3", "output", "...");
        Inspector.AddLabel("udr3", "frames", "...");
        Inspector.AddDropdown("udr3", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, (index) => UDRQuality.Set(index));
        UDRQuality.Changed += _ => Inspector.SetDropdownValue("udr3", "qualityDrop", UDRQuality.Index);
        Inspector.AddToggle("udr3", "debugEdges", "Debug Edges", false, (enabled) => DebugEdges = enabled ? 1 : 0);
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    private void CreateRenderTargets()
    {
        SpatialTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        EdgeTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        TemporalTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        LastFrameTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);
    }

    private void ApplyRenderScale()
    {
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
    }

    public override void Update()
    {
        if (InputSource == null)
            return;

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

        Inspector.SetLabel("udr3", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("udr3", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("udr3", "output", $"Output Size: {OutputSize.X}x{OutputSize.Y}");
        Inspector.SetLabel("udr3", "frames", $"Frames to Accumulate: {FramesToAccumulate} (current: {effectiveFrames})");
    }

    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100)
            return;

        Renderer.Blit(EdgeTexture, BlendState.AlphaBlend, SamplerState.LinearClamp);
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
