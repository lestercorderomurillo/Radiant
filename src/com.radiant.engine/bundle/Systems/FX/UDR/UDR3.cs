using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class UDR3 : core.System
{

    // Parameters
    public float DetailCorrection = 0f;
    public bool DebugRays = false;

    private RenderTarget2D OutputTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    private int FrameCount = 0;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Geometry = Scene.ECS.GetSystem<Geometry>();

        if (Geometry != null)
            Geometry.EnableSDF = true;

        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();
        PrevKeyState = Keyboard.GetState();
        FrameCount = 0;
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    private void CreateRenderTargets()
    {
        OutputTexture = new RenderTarget2D(
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

        // Single pass: Bilinear upscaling
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("Bilinear")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("LastFrame", OutputTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("DetailCorrection", DetailCorrection)
            .SetParameter("DebugRays", DebugRays ? 1f : 0f)
            .SetParameter("FrameCount", (float)FrameCount)
            .Draw()
            .Commit()
            .SetTarget(null);

        FrameCount++;

        Gizmos?.Set("UDR3", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0}) [F4]");
        Gizmos?.Set("UDR3", $"Input Size: {input.Width}x{input.Height}");
        Gizmos?.Set("UDR3", $"Output Size: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("UDR3", $"Detail Correction: {DetailCorrection:F2}");
        Gizmos?.Set("UDR3", $"Debug Rays: {(DebugRays ? "On" : "Off")} [F10]");
        Gizmos?.Set("UDR3", $"Frame Count: {FrameCount}");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        // F4 to cycle quality
        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            UDRQuality.Cycle();
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

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(OutputTexture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    public RenderTarget2D GetOutput() => OutputTexture;

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;
        if (OutputSize == newSize)
            return;

        DisposeRenderTargets();
        OutputSize = newSize;
        CreateRenderTargets();
        FrameCount = 0;
    }

    private void DisposeRenderTargets()
    {
        OutputTexture?.Dispose();
    }

    public override void Dispose()
    {
        // Restore render scale to 1.0 when UDR3 is disabled
        Renderer.RenderScale = 1.0f;

        DisposeRenderTargets();
        OutputTexture = null;
    }
}
