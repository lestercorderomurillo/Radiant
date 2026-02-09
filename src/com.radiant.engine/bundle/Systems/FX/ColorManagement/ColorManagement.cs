using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(HRCGI))]
[RunAfter(typeof(RCGI))]
[RunBefore(typeof(Bilinear))]
[RunBefore(typeof(UDR1))]
[RunBefore(typeof(UDR2))]
[RunBefore(typeof(UDR3))]
public class ColorManagement : core.System
{
    private static readonly string[] TechniqueNames = ["None", "ACES", "ACES2", "AgX"];
    private int TechniqueIndex = 2;
    private Func<Texture2D> InputSource;
    private RenderTarget2D OutputTexture;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        PrevKeyState = Keyboard.GetState();
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    public Texture2D GetOutput()
    {
        if (OutputTexture != null)
            return OutputTexture;
        return InputSource?.Invoke();
    }

    public override void Update()
    {
        HandleInput();

        if (InputSource == null)
            return;

        var input = InputSource();
        if (input == null)
            return;

        EnsureRenderTarget(input.Width, input.Height);

        Renderer
            .Reset()
            .SetShader("ColorManagement")
            .SetTechnique(TechniqueNames[TechniqueIndex])
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .Draw()
            .Commit()
            .SetTarget(null);

        Gizmos?.Set("ColorMgmt", $"Tonemapping: {TechniqueNames[TechniqueIndex]} [F6]");
    }

    public override void Render()
    {
        if (InputSource == null || OutputTexture == null)
            return;

        Renderer.Blit(OutputTexture, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private void HandleInput()
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F6) && !PrevKeyState.IsKeyDown(Keys.F6))
            TechniqueIndex = (TechniqueIndex + 1) % TechniqueNames.Length;
        PrevKeyState = keyboard;
    }

    private void EnsureRenderTarget(int Width, int Height)
    {
        if (OutputTexture != null && OutputTexture.Width == Width && OutputTexture.Height == Height)
            return;

        OutputTexture?.Dispose();
        OutputTexture = Renderer.CreateRenderTarget(Width, Height, SurfaceFormat.Color);
    }

    public override void OnResize()
    {
        OutputTexture?.Dispose();
        OutputTexture = null;
    }

    public override void Dispose()
    {
        OutputTexture?.Dispose();
        OutputTexture = null;
    }
}
