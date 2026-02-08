using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class UDR1 : core.System
{

    public float Sharpness = 0.5f;
    public bool EdgeCorrection = true;
    public bool DebugRays = false;

    private RenderTarget2D OutputTexture;
    private RenderTarget2D SmoothTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();
        PrevKeyState = Keyboard.GetState();
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    private void CreateRenderTargets()
    {
        OutputTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        SmoothTexture = Renderer.CreateRenderTarget(
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

        HandleInput();

        // Skip UDR1 processing when Off (native resolution)
        /*if (ScaleFactor == 100)
        {
            Gizmos?.Set("UDR1", $"Quality: {QualityNames[QualityIndex]} [F4]");
            return;
        }*/

        var input = InputSource();

        if (input == null)
            return;

        Vector2 inputSize = new Vector2(input.Width, input.Height);

        Renderer
            .Reset()
            .SetShader("UDR/UDR1")
            .SetTechnique("Upscale")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("AbsorptionTexture", Geometry?.AbsorptionTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .SetParameter("EdgeCorrection", EdgeCorrection ? 1f : 0f)
            .SetParameter("DebugRays", DebugRays ? 1f : 0f)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Edge smoothing pass (always on)
        Renderer
            .Reset()
            .SetShader("UDR/UDR1")
            .SetTechnique("EdgeSmooth")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SmoothTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", OutputTexture)
            .SetParameter("OutputSize", OutputSize)
            .Draw()
            .Commit()
            .SetTarget(null);

        Gizmos?.Set("UDR1", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0}) [F4]");
        Gizmos?.Set("UDR1", $"Input Size: {input.Width}x{input.Height}");
        Gizmos?.Set("UDR1", $"Output Size: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("UDR1", $"Sharpness: {Sharpness:F2} [F7/F8]");
        Gizmos?.Set("UDR1", $"Detail Reconstruction: {(EdgeCorrection ? "On" : "Off")} [F9]");
        Gizmos?.Set("UDR1", $"Debug Rays: {(DebugRays ? "On" : "Off")} [F10]");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        // F4 to cycle quality
        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            UDRQuality.Cycle();
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

        // F9 to toggle edge overlay
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
        if (InputSource == null || UDRQuality.ScaleFactor == 100)
            return;

        Renderer.Blit(SmoothTexture, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    public RenderTarget2D GetOutput() => SmoothTexture;

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;
        if (OutputSize == newSize)
            return;

        OutputTexture?.Dispose();
        SmoothTexture?.Dispose();
        OutputSize = newSize;
        CreateRenderTargets();
    }

    public override void Dispose()
    {
        // Restore render scale to 1.0 when UDR1 is disabled
        Renderer.RenderScale = 1.0f;

        OutputTexture?.Dispose();
        OutputTexture = null;

        SmoothTexture?.Dispose();
        SmoothTexture = null;
    }
}
