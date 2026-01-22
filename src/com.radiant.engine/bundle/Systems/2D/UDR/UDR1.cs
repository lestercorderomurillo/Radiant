using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class UDR1 : core.System
{
    private static readonly int[] ScaleFactors = { 100, 88, 77, 50, 33, 25 };
    private static readonly string[] QualityNames = { "Off", "Ultra Quality", "Quality", "Balanced", "Performance", "Ultra Performance" };
    private int QualityIndex = 0;  // Default to Off (native)
    private int ScaleFactor => ScaleFactors[QualityIndex];

    public float Sharpness = 0.5f;
    public bool EdgeOverlay = false;

    private RenderTarget2D OutputTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private SceneGeometry SceneGeometry;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        SceneGeometry = Scene.ECS.GetSystem<SceneGeometry>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
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

        // Skip UDR1 processing when Off (native resolution)
        if (QualityIndex == 0)
        {
            Gizmos?.Set("UDR1", $"Quality: {QualityNames[QualityIndex]} [F4]");
            return;
        }

        var input = InputSource();
        if (input == null)
            return;

        Vector2 inputSize = new Vector2(input.Width, input.Height);

        Renderer
            .Reset()
            .SetShader("UDR/UDR1")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("EmissiveTexture", SceneGeometry?.EmissiveTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .SetParameter("EdgeOverlay", EdgeOverlay ? 1f : 0f)
            .Draw()
            .Commit()
            .SetTarget(null);

        Gizmos?.Set("UDR1", $"Quality: {QualityNames[QualityIndex]} ({GetScaleFactorNormalized():P0}) [F4]");
        Gizmos?.Set("UDR1", $"Input: {input.Width}x{input.Height}");
        Gizmos?.Set("UDR1", $"Output: {OutputSize.X}x{OutputSize.Y}");
        Gizmos?.Set("UDR1", $"Sharpness: {Sharpness:F2} [F7/F8]");
        Gizmos?.Set("UDR1", $"EdgeOverlay: {(EdgeOverlay ? "On" : "Off")} [F9]");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        // F4 to cycle UDR1 quality
        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            QualityIndex = (QualityIndex + 1) % ScaleFactors.Length;
            ApplyRenderScale();
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
            EdgeOverlay = !EdgeOverlay;
        }

        PrevKeyState = key;
    }

    public override void Render()
    {
        if (InputSource == null || QualityIndex == 0)
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
        // Restore render scale to 1.0 when UDR1 is disabled
        Renderer.RenderScale = 1.0f;

        OutputTexture?.Dispose();
        OutputTexture = null;
    }
}
