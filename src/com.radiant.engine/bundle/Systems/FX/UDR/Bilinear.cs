using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class Bilinear : core.System
{
    private Func<Texture2D> InputSource;
    private GizmosRenderer Gizmos;
    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        ApplyRenderScale();
        UDRQuality.Changed += _ => ApplyRenderScale();
        PrevKeyState = Keyboard.GetState();
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

        HandleInput();

        var input = InputSource();
        if (input == null)
            return;

        Renderer.SetTarget(null);

        Gizmos?.Set("Bilinear", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0}) [F4]");
        Gizmos?.Set("Bilinear", $"Input Size: {input.Width}x{input.Height}");
        Gizmos?.Set("Bilinear", $"Output Size: {Renderer.ScreenSize.X}x{Renderer.ScreenSize.Y}");
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();

        if (key.IsKeyDown(Keys.F4) && !PrevKeyState.IsKeyDown(Keys.F4))
        {
            UDRQuality.Cycle();
        }

        PrevKeyState = key;
    }

    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100)
            return;

        var input = InputSource();
        if (input == null)
            return;

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(input, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    public override void Dispose()
    {
        Renderer.RenderScale = 1.0f;
    }
}
