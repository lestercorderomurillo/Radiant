using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class Bilinear : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    private Func<Texture2D> InputSource;

    public override void Initialize()
    {
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();

        Inspector.CreateWindow("bilinear", "Bilinear");
        Inspector.AddLabel("bilinear", "quality", "...");
        Inspector.AddLabel("bilinear", "input", "...");
        Inspector.AddLabel("bilinear", "output", "...");
        Inspector.AddButton("bilinear", "cycleQuality", "Cycle Quality", () => UDRQuality.Cycle());
    }

    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
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

        Renderer.SetTarget(null);

        Inspector.SetLabel("bilinear", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("bilinear", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("bilinear", "output", $"Output Size: {Renderer.ScreenSize.X}x{Renderer.ScreenSize.Y}");
    }

    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100)
            return;

        var input = InputSource();
        if (input == null)
            return;

        Renderer.Blit(input, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    public override void Dispose()
    {
        Renderer.RenderScale = 1.0f;
    }
}
