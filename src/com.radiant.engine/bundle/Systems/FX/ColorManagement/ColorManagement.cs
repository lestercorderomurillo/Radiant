using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

    public override void Initialize()
    {
        UISystem.CreateWindow("colormgmt", "Color Management", new Vector2(380, 230), new Vector2(340, 0));
        UISystem.AddLabel("colormgmt", "mode", $"Tonemapping: {TechniqueNames[TechniqueIndex]}");
        UISystem.AddButton("colormgmt", "cycle", "Cycle Tonemapping", () =>
            TechniqueIndex = (TechniqueIndex + 1) % TechniqueNames.Length);
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

        UISystem.SetLabel("colormgmt", "mode", $"Tonemapping: {TechniqueNames[TechniqueIndex]}");
    }

    public override void Render()
    {
        if (InputSource == null || OutputTexture == null)
            return;

        Renderer.Blit(OutputTexture, BlendState.AlphaBlend, SamplerState.PointClamp);
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
