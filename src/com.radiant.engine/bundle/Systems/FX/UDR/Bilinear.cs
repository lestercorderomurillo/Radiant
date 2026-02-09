using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class Bilinear : core.System
{
    private Func<Texture2D> InputSource;

    public override void Initialize()
    {
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();

        UISystem.CreateWindow("bilinear", "Bilinear", new Vector2(380, 370), new Vector2(340, 0));
        UISystem.AddLabel("bilinear", "quality", "...");
        UISystem.AddLabel("bilinear", "input", "...");
        UISystem.AddLabel("bilinear", "output", "...");
        UISystem.AddButton("bilinear", "cycleQuality", "Cycle Quality", () => UDRQuality.Cycle());
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

        UISystem.SetLabel("bilinear", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        UISystem.SetLabel("bilinear", "input", $"Input Size: {input.Width}x{input.Height}");
        UISystem.SetLabel("bilinear", "output", $"Output Size: {Renderer.ScreenSize.X}x{Renderer.ScreenSize.Y}");
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
