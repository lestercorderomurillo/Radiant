using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(HRCGI), typeof(RCGI))]
[RunBefore(typeof(Bilinear), typeof(UDR1), typeof(UDR2), typeof(UDR3))]
public class ColorManagement : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    private static readonly string[] TechniqueNames = ["None", "ACES", "ACES2", "AgX", "Filmic"];
    private int TechniqueIndex = 2;
    private Func<Texture2D> InputSource;
    private RenderTarget2D OutputTexture;

    public override void Initialize()
    {
        Inspector.CreateWindow("colormgmt", "Color Management");
        Inspector.AddDropdown("colormgmt", "tonemap", "Tonemapping", TechniqueNames, TechniqueIndex, (index) => TechniqueIndex = index);
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
