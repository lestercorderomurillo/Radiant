using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class FSR : core.System
{
    private FSRQuality _quality = FSRQuality.Off;
    public FSRQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value)
            {
                _quality = value;
                ApplyRenderScale();
            }
        }
    }

    public float Sharpness = 0.5f;

    private RenderTarget2D OutputTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private GizmosRenderer Gizmos;
    private float PreviousRenderScale;
    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();

        PreviousRenderScale = Renderer.RenderScale;
        ApplyRenderScale();

        PrevKeyState = Keyboard.GetState();
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

    private float GetScaleFactor() => (int)Quality / 100f;

    private void ApplyRenderScale()
    {
        Renderer.RenderScale = GetScaleFactor();
    }

    public override void Update()
    {
        if (InputSource == null)
            return;

        HandleInput();

        // Skip FSR processing when Off (native resolution)
        if (Quality == FSRQuality.Off)
        {
            Gizmos?.Set("FSR", "Quality: Off (Native) [F4]");
            return;
        }

        var input = InputSource();
        if (input == null)
            return;

        Vector2 inputSize = new Vector2(input.Width, input.Height);

        Renderer
            .Reset()
            .SetShader("FSR/FSR_Upscale")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .Draw()
            .Commit()
            .SetTarget(null);

        Gizmos?.Set("FSR", $"Quality: {Quality} ({GetScaleFactor():P0}) [F4]");
        Gizmos?.Set("FSR", $"Input: {input.Width}x{input.Height}");
        Gizmos?.Set("FSR", $"Output: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("FSR", $"Sharpness: {Sharpness:F2} [F7/F8]");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        // F7/F8 to adjust sharpness
        if (key.IsKeyDown(Keys.F7) && !PrevKeyState.IsKeyDown(Keys.F7))
        {
            Sharpness = Math.Max(0f, Sharpness - 0.1f);
        }
        if (key.IsKeyDown(Keys.F8) && !PrevKeyState.IsKeyDown(Keys.F8))
        {
            Sharpness = Math.Min(2f, Sharpness + 0.1f);
        }

        PrevKeyState = key;
    }

    public override void Render()
    {
        if (InputSource == null || Quality == FSRQuality.Off)
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

        OutputTexture?.Dispose();
        OutputSize = newSize;
        CreateRenderTargets();
    }

    public override void Dispose()
    {
        // Restore render scale to 1.0 when FSR is disabled
        Renderer.RenderScale = 1.0f;

        OutputTexture?.Dispose();
        OutputTexture = null;
    }
}
