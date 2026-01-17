using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class HolographicRC : core.System
{
    private const int FrustumCount = 4;
    private const int CascadeCount = 4;

    private SceneGeometry SDFSystem;
    private GizmosRenderer Gizmos;
    private Effect Shader;
    private SpriteBatch ShaderBatch;
    private Texture2D PixelTexture;

    private RenderTarget2D[,] Cascades;
    private RenderTarget2D[] Resolved;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;

    private KeyboardState PrevKeyState;
    private int DebugIndex = 0;
    private string[] DebugNames;

    public override void Initialize()
    {
        base.Initialize();
        SDFSystem = Scene.ECS.GetSystem<SceneGeometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        var device = Renderer.Device;

        Shader = Renderer.Window.Content.Load<Effect>("shaders/HRC");
        ShaderBatch = new SpriteBatch(device);
        PixelTexture = new Texture2D(device, 1, 1);
        PixelTexture.SetData([Color.White]);

        WorldSize = new Vector2(device.Viewport.Width, device.Viewport.Height);

        CalculateCascadeSizes();
        CreateRenderTargets();
        BuildDebugNames();

        Gizmos.AddSection("HRC", "HRC", Color.Cyan);
        PrevKeyState = Keyboard.GetState();
    }

    private void CalculateCascadeSizes()
    {
        CascadeSizes = new Vector2[CascadeCount];
        for (int c = 0; c < CascadeCount; c++)
        {
            float interval = MathF.Pow(2, c);
            int numProbes = (int)MathF.Floor(WorldSize.X / interval);
            CascadeSizes[c] = new Vector2(numProbes * interval, WorldSize.Y);
        }
    }

    private void CreateRenderTargets()
    {
        var device = Renderer.Device;
        var format = SurfaceFormat.Color;

        Cascades = new RenderTarget2D[FrustumCount, CascadeCount];
        Resolved = new RenderTarget2D[FrustumCount];

        for (int f = 0; f < FrustumCount; f++)
        {
            for (int c = 0; c < CascadeCount; c++)
            {
                int w = (int)CascadeSizes[c].X;
                int h = (int)CascadeSizes[c].Y;
                Cascades[f, c] = new RenderTarget2D(device, w, h, false, format, DepthFormat.None);
            }
            Resolved[f] = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
        }
        FinalTexture = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
    }

    private void BuildDebugNames()
    {
        var names = new System.Collections.Generic.List<string> { "Final" };
        for (int f = 0; f < FrustumCount; f++) names.Add($"F{f}");
        for (int f = 0; f < FrustumCount; f++)
            for (int c = 0; c < CascadeCount; c++)
                names.Add($"F{f}C{c}");
        names.Add("Emissive");
        names.Add("Absorption");
        DebugNames = names.ToArray();
    }

    private void SetSamplers()
    {
        var device = Renderer.Device;
        for (int i = 1; i <= 10; i++)
            device.SamplerStates[i] = SamplerState.LinearClamp;
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F3) && !PrevKeyState.IsKeyDown(Keys.F3))
            DebugIndex = (DebugIndex + 1) % DebugNames.Length;
        PrevKeyState = keyboard;

        var emissive = SDFSystem.GetEmissiveTexture();
        var absorption = SDFSystem.GetAbsorptionTexture();

        for (int f = 0; f < FrustumCount; f++)
        {
            for (int c = 0; c < CascadeCount; c++)
            {
                RenderCascade(f, c, emissive, absorption);
            }

            CopyTexture(Cascades[f, CascadeCount - 1], Resolved[f]);
        }

        Compose();

        Gizmos.ClearSection("HRC");
        Gizmos.AddSectionString("HRC", $"{DebugNames[DebugIndex]} (F3)");
    }

    private void RenderCascade(int frustum, int cascade, Texture2D emissive, Texture2D absorption)
    {
        var device = Renderer.Device;
        device.SetRenderTarget(Cascades[frustum, cascade]);
        device.Clear(Color.Transparent);
        SetSamplers();

        Renderer.SetParameter(Shader, "EmissiveTex", emissive);
        Renderer.SetParameter(Shader, "AbsorpTex", absorption);
        Renderer.SetParameter(Shader, "WorldSize", WorldSize);
        Renderer.SetParameter(Shader, "CascadeSize", CascadeSizes[cascade]);
        Renderer.SetParameter(Shader, "CascadeIndex", (float)cascade);
        Renderer.SetParameter(Shader, "Frustum", (float)frustum);

        if (cascade > 0)
        {
            Renderer.SetParameter(Shader, "PrevCasc", Cascades[frustum, cascade - 1]);
            Renderer.SetParameter(Shader, "PrevSize", CascadeSizes[cascade - 1]);
        }
        else
        {
            Renderer.SetParameter(Shader, "PrevCasc", PixelTexture);
            Renderer.SetParameter(Shader, "PrevSize", new Vector2(1, 1));
        }

        Shader.CurrentTechnique = Shader.Techniques["Merge"];
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, Shader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y), Color.White);
        ShaderBatch.End();

        device.SetRenderTarget(null);
    }

    private void CopyTexture(RenderTarget2D source, RenderTarget2D destination)
    {
        var device = Renderer.Device;
        device.SetRenderTarget(destination);
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp);
        ShaderBatch.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
        ShaderBatch.End();
        device.SetRenderTarget(null);
    }

    private void Compose()
    {
        var device = Renderer.Device;
        device.SetRenderTarget(FinalTexture);
        device.Clear(Color.Black);
        SetSamplers();

        Renderer.SetParameter(Shader, "Frust0", Resolved[0]);
        Renderer.SetParameter(Shader, "Frust1", Resolved[1]);
        Renderer.SetParameter(Shader, "Frust2", Resolved[2]);
        Renderer.SetParameter(Shader, "Frust3", Resolved[3]);
        Renderer.SetParameter(Shader, "CascadeSize", WorldSize);
        Shader.CurrentTechnique = Shader.Techniques["Compose"];

        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, Shader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)WorldSize.X, (int)WorldSize.Y), Color.White);
        ShaderBatch.End();
        device.SetRenderTarget(null);
    }

    public override void Render()
    {
        Texture2D texture = GetDebugTexture();
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(texture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    private Texture2D GetDebugTexture()
    {
        if (DebugIndex == 0) return FinalTexture;
        int index = DebugIndex - 1;
        if (index < FrustumCount) return Resolved[index];
        index -= FrustumCount;
        if (index < FrustumCount * CascadeCount) return Cascades[index / CascadeCount, index % CascadeCount];
        index -= FrustumCount * CascadeCount;
        if (index == 0) return SDFSystem.GetEmissiveTexture();
        return SDFSystem.GetAbsorptionTexture();
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void Dispose()
    {
        Shader?.Dispose();
        ShaderBatch?.Dispose();
        PixelTexture?.Dispose();
        FinalTexture?.Dispose();

        for (int f = 0; f < FrustumCount; f++)
        {
            Resolved[f]?.Dispose();
            for (int c = 0; c < CascadeCount; c++)
                Cascades[f, c]?.Dispose();
        }
    }
}
