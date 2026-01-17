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
    private Effect FrustumSeedShader;
    private Effect ExtensionsShader;
    private Effect MergingConesShader;
    private Effect FluenceSumShader;
    private SpriteBatch ShaderBatch;
    private Texture2D PixelTexture;

    // Vrays buffers: store ray extensions per frustum per cascade
    // Packed format: RGB = radiance, A = transmittance
    private RenderTarget2D[,] Vrays;

    // Merge buffers: store merged cone results per frustum per cascade
    // Packed format: RGB = radiance, A = transmittance
    private RenderTarget2D[,] Merge;

    // Per-frustum resolved output and final composited result
    private RenderTarget2D[] FrustumOutput;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    private KeyboardState PrevKeyState;
    private int DebugIndex = 0;
    private string[] DebugNames;

    public override void Initialize()
    {
        base.Initialize();
        SDFSystem = Scene.ECS.GetSystem<SceneGeometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        FrustumSeedShader = Renderer.GetShaderEffect("HRC/HRC_FrustumSeed");
        ExtensionsShader = Renderer.GetShaderEffect("HRC/HRC_Extensions");
        MergingConesShader = Renderer.GetShaderEffect("HRC/HRC_MergingCones");
        FluenceSumShader = Renderer.GetShaderEffect("HRC/HRC_FluenceSum");
        ShaderBatch = Renderer.SpriteBatch;
        PixelTexture = Renderer.PixelTexture;

        WorldSize = new Vector2(Renderer.Device.Viewport.Width, Renderer.Device.Viewport.Height);

        CalculateCascadeSizes();
        CreateRenderTargets();
        BuildDebugNames();

        Gizmos.AddSection("HRC", "HRC", Color.Cyan);
        PrevKeyState = Keyboard.GetState();
    }

    private void CalculateCascadeSizes()
    {
        CascadeSizes = new Vector2[CascadeCount];
        MergeSizes = new Vector2[CascadeCount];

        for (int c = 0; c < CascadeCount; c++)
        {
            float interval = MathF.Pow(2, c);
            float virtualRays = interval + 1;
            int numProbes = (int)MathF.Floor(WorldSize.X / interval);

            // Vrays size: numProbes * virtualRays wide
            CascadeSizes[c] = new Vector2(numProbes * virtualRays, WorldSize.Y);

            // Merge size: numProbes * interval wide (cones, not rays)
            MergeSizes[c] = new Vector2(numProbes * interval, WorldSize.Y);
        }
    }

    private void CreateRenderTargets()
    {
        var device = Renderer.Device;
        // Use HalfVector4 for sufficient bit-depth in radiance cascade math
        // Standard Color (RGBA8) lacks precision and causes heavy banding
        var format = SurfaceFormat.HalfVector4;

        Vrays = new RenderTarget2D[FrustumCount, CascadeCount];
        Merge = new RenderTarget2D[FrustumCount, CascadeCount];
        FrustumOutput = new RenderTarget2D[FrustumCount];

        for (int f = 0; f < FrustumCount; f++)
        {
            for (int c = 0; c < CascadeCount; c++)
            {
                int vw = (int)CascadeSizes[c].X;
                int vh = (int)CascadeSizes[c].Y;
                Vrays[f, c] = new RenderTarget2D(device, vw, vh, false, format, DepthFormat.None);

                int mw = (int)MergeSizes[c].X;
                int mh = (int)MergeSizes[c].Y;
                Merge[f, c] = new RenderTarget2D(device, mw, mh, false, format, DepthFormat.None);
            }
            FrustumOutput[f] = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
        }
        FinalTexture = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
    }

    private void BuildDebugNames()
    {
        var names = new System.Collections.Generic.List<string> { "Final" };
        // C0 first for all frustums, then C1, etc.
        for (int c = 0; c < CascadeCount; c++)
            for (int f = 0; f < FrustumCount; f++)
                names.Add($"Vrays F{f}C{c}");
        for (int c = 0; c < CascadeCount; c++)
            for (int f = 0; f < FrustumCount; f++)
                names.Add($"Merge F{f}C{c}");
        for (int f = 0; f < FrustumCount; f++) names.Add($"Frustum{f}");
        names.Add("Emissive");
        names.Add("Absorption");
        DebugNames = names.ToArray();
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
            // Step 1: Seed cascade 0 with FrustumSeed shader
            RenderFrustumSeed(f, emissive, absorption);

            // Step 2: Extend rays for cascades 1 to N-1
            for (int c = 1; c < CascadeCount; c++)
            {
                RenderExtensions(f, c);
            }

            // Step 3: Merge cones from cascade N-1 down to 0
            for (int c = CascadeCount - 1; c >= 0; c--)
            {
                RenderMerging(f, c);
            }

            // Step 4: Copy merge result of cascade 0 to frustum output
            CopyTexture(Merge[f, 0], FrustumOutput[f]);
        }

        // Step 5: Sum all 4 frustums
        Compose();

        Gizmos.ClearSection("HRC");
        Gizmos.AddSectionString("HRC", $"{DebugNames[DebugIndex]} (F3)");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        var device = Renderer.Device;
        var size = CascadeSizes[0];

        device.SetRenderTarget(Vrays[frustum, 0]);
        device.Clear(Color.Black);

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;

        device.Textures[1] = emissive;
        device.Textures[2] = absorption;
        // Use PointClamp to prevent light leaking between angular probes
        device.SamplerStates[0] = SamplerState.PointClamp;
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;

        Renderer.SetParameter(FrustumSeedShader, "Emissivity", emissive);
        Renderer.SetParameter(FrustumSeedShader, "Absorption", absorption);
        Renderer.SetParameter(FrustumSeedShader, "WorldSize", WorldSize);
        Renderer.SetParameter(FrustumSeedShader, "CascadeSize", size);
        Renderer.SetParameter(FrustumSeedShader, "FrustumIndex", (float)frustum);

        FrustumSeedShader.CurrentTechnique = FrustumSeedShader.Techniques["GenerateOutputTexture"];
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, FrustumSeedShader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        ShaderBatch.End();

        device.SetRenderTarget(null);
        device.Textures[1] = null;
        device.Textures[2] = null;
    }

    private void RenderExtensions(int frustum, int cascade)
    {
        var device = Renderer.Device;
        var size = CascadeSizes[cascade];
        var prevSize = CascadeSizes[cascade - 1];

        device.SetRenderTarget(Vrays[frustum, cascade]);
        device.Clear(Color.Transparent);

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;

        // Use PointClamp to prevent light leaking between angular probes
        device.SamplerStates[1] = SamplerState.PointClamp;

        // Set parameters before Begin
        ExtensionsShader.Parameters["PrevSize"]?.SetValue(prevSize);
        ExtensionsShader.Parameters["CascadeSize"]?.SetValue(size);
        ExtensionsShader.Parameters["CascadeIndex"]?.SetValue(new Vector2(cascade, CascadeCount));
        ExtensionsShader.Parameters["PrevCascade"]?.SetValue(Vrays[frustum, cascade - 1]);

        ExtensionsShader.CurrentTechnique = ExtensionsShader.Techniques["GenerateOutputTexture"];
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, ExtensionsShader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        ShaderBatch.End();

        device.SetRenderTarget(null);
        device.Textures[1] = null;
    }

    private void RenderMerging(int frustum, int cascade)
    {
        var device = Renderer.Device;
        var vraysSize = CascadeSizes[cascade];
        var mergeSize = MergeSizes[cascade];

        // For cascade N-1 (highest), there's no "previous" merge, use empty/default
        // For lower cascades, use the merged result from cascade+1
        int nextCascade = (cascade + 1) % CascadeCount;
        var prevMergeSize = MergeSizes[nextCascade];

        device.SetRenderTarget(Merge[frustum, cascade]);
        device.Clear(Color.Transparent);

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;

        // Use PointClamp to prevent light leaking between angular probes
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;

        // Set parameters via shader - same pattern that works for Extensions
        MergingConesShader.Parameters["VraysCascade"]?.SetValue(Vrays[frustum, cascade]);
        MergingConesShader.Parameters["PrevMerge"]?.SetValue(Merge[frustum, nextCascade]);
        MergingConesShader.Parameters["VraysSize"]?.SetValue(vraysSize);
        MergingConesShader.Parameters["PrevSize"]?.SetValue(prevMergeSize);
        MergingConesShader.Parameters["CascadeSize"]?.SetValue(mergeSize);
        MergingConesShader.Parameters["CascadeIndex"]?.SetValue(new Vector2(cascade, CascadeCount));

        MergingConesShader.CurrentTechnique = MergingConesShader.Techniques["GenerateOutputTexture"];
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, MergingConesShader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)mergeSize.X, (int)mergeSize.Y), Color.White);
        ShaderBatch.End();

        device.SetRenderTarget(null);
        device.Textures[1] = null;
        device.Textures[2] = null;
    }

    private void CopyTexture(RenderTarget2D source, RenderTarget2D destination)
    {
        var device = Renderer.Device;
        device.SetRenderTarget(destination);
        device.Clear(Color.Transparent);
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        ShaderBatch.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
        ShaderBatch.End();
        device.SetRenderTarget(null);
    }

    private void Compose()
    {
        var device = Renderer.Device;
        device.SetRenderTarget(FinalTexture);
        device.Clear(Color.Black);

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;

        device.Textures[1] = FrustumOutput[0];
        device.Textures[2] = FrustumOutput[1];
        device.Textures[3] = FrustumOutput[2];
        device.Textures[4] = FrustumOutput[3];
        // Use PointClamp to prevent light leaking between angular probes
        device.SamplerStates[0] = SamplerState.PointClamp;
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;
        device.SamplerStates[3] = SamplerState.PointClamp;
        device.SamplerStates[4] = SamplerState.PointClamp;

        Renderer.SetParameter(FluenceSumShader, "FrustumIndex0", FrustumOutput[0]);
        Renderer.SetParameter(FluenceSumShader, "FrustumIndex1", FrustumOutput[1]);
        Renderer.SetParameter(FluenceSumShader, "FrustumIndex2", FrustumOutput[2]);
        Renderer.SetParameter(FluenceSumShader, "FrustumIndex3", FrustumOutput[3]);
        Renderer.SetParameter(FluenceSumShader, "WorldSize", WorldSize);

        FluenceSumShader.CurrentTechnique = FluenceSumShader.Techniques["GenerateOutputTexture"];
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, FluenceSumShader);
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)WorldSize.X, (int)WorldSize.Y), Color.White);
        ShaderBatch.End();
        device.SetRenderTarget(null);
        device.Textures[1] = null;
        device.Textures[2] = null;
        device.Textures[3] = null;
        device.Textures[4] = null;
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

        // Vrays: C0 for all frustums first, then C1, etc.
        if (index < FrustumCount * CascadeCount)
        {
            int c = index / FrustumCount;
            int f = index % FrustumCount;
            return Vrays[f, c];
        }
        index -= FrustumCount * CascadeCount;

        // Merge: C0 for all frustums first, then C1, etc.
        if (index < FrustumCount * CascadeCount)
        {
            int c = index / FrustumCount;
            int f = index % FrustumCount;
            return Merge[f, c];
        }
        index -= FrustumCount * CascadeCount;

        // Frustum outputs
        if (index < FrustumCount) return FrustumOutput[index];
        index -= FrustumCount;

        // Emissive and Absorption
        if (index == 0) return SDFSystem.GetEmissiveTexture();
        return SDFSystem.GetAbsorptionTexture();
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void Dispose()
    {
        FinalTexture?.Dispose();

        for (int f = 0; f < FrustumCount; f++)
        {
            FrustumOutput[f]?.Dispose();
            for (int c = 0; c < CascadeCount; c++)
            {
                Vrays[f, c]?.Dispose();
                Merge[f, c]?.Dispose();
            }
        }
    }
}
