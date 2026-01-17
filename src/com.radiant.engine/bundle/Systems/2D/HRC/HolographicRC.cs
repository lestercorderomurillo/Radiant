using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

/// <summary>
/// Holographic Radiance Cascades - 2D Global Illumination System
/// Based on the paper by Rouli Freeman (arXiv:2505.02041)
///
/// Uses MRT (Multiple Render Targets) for single-pass radiance+transmittance output.
/// </summary>
public class HolographicRC : core.System
{
    private const int FrustumCount = 4;
    private const int CascadeCount = 6;

    private SceneGeometry SDFSystem;
    private GizmosRenderer Gizmos;

    private Effect FrustumSeedShader;
    private Effect ExtensionsShader;
    private Effect MergingConesShader;
    private Effect FluenceSumShader;

    private SpriteBatch ShaderBatch;
    private Texture2D PixelTexture;

    private Texture2D BlackTexture;
    private Texture2D WhiteTexture;

    // Dual textures: [frustum, cascade]
    private RenderTarget2D[,] VraysRadiance;
    private RenderTarget2D[,] VraysTransmit;
    private RenderTarget2D[,] MergeRadiance;
    private RenderTarget2D[,] MergeTransmit;

    // MRT binding arrays (reusable to avoid GC)
    private RenderTargetBinding[] MRT2;

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

        BlackTexture = new Texture2D(Renderer.Device, 1, 1);
        BlackTexture.SetData(new[] { Color.Black });

        WhiteTexture = new Texture2D(Renderer.Device, 1, 1);
        WhiteTexture.SetData(new[] { Color.White });

        // MRT binding array
        MRT2 = new RenderTargetBinding[2];

        var device = Renderer.Device;
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
        MergeSizes = new Vector2[CascadeCount];

        for (int c = 0; c < CascadeCount; c++)
        {
            float interval = MathF.Pow(2, c);
            float virtualRays = interval + 1;
            int numProbes = (int)MathF.Ceiling(WorldSize.X / interval);

            CascadeSizes[c] = new Vector2(numProbes * virtualRays, WorldSize.Y);
            MergeSizes[c] = new Vector2(numProbes * interval, WorldSize.Y);
        }
    }

    private void CreateRenderTargets()
    {
        var device = Renderer.Device;
        var format = SurfaceFormat.HalfVector4;

        VraysRadiance = new RenderTarget2D[FrustumCount, CascadeCount];
        VraysTransmit = new RenderTarget2D[FrustumCount, CascadeCount];
        MergeRadiance = new RenderTarget2D[FrustumCount, CascadeCount];
        MergeTransmit = new RenderTarget2D[FrustumCount, CascadeCount];
        FrustumOutput = new RenderTarget2D[FrustumCount];

        for (int f = 0; f < FrustumCount; f++)
        {
            for (int c = 0; c < CascadeCount; c++)
            {
                VraysRadiance[f, c] = new RenderTarget2D(device, (int)CascadeSizes[c].X, (int)CascadeSizes[c].Y, false, format, DepthFormat.None);
                VraysTransmit[f, c] = new RenderTarget2D(device, (int)CascadeSizes[c].X, (int)CascadeSizes[c].Y, false, format, DepthFormat.None);
                MergeRadiance[f, c] = new RenderTarget2D(device, (int)MergeSizes[c].X, (int)MergeSizes[c].Y, false, format, DepthFormat.None);
                MergeTransmit[f, c] = new RenderTarget2D(device, (int)MergeSizes[c].X, (int)MergeSizes[c].Y, false, format, DepthFormat.None);
            }
            FrustumOutput[f] = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
        }
        FinalTexture = new RenderTarget2D(device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
    }

    public override void Update()
    {
        HandleDebugInput();

        var emissive = SDFSystem.GetEmissiveTexture();
        var absorption = SDFSystem.GetAbsorptionTexture();
        var device = Renderer.Device;

        var originalTargets = device.GetRenderTargets();

        for (int f = 0; f < FrustumCount; f++)
        {
            RenderFrustumSeed(f, emissive, absorption);

            for (int c = 1; c < CascadeCount; c++)
            {
                RenderExtensions(f, c);
            }

            for (int c = CascadeCount - 1; c >= 0; c--)
            {
                RenderMerging(f, c);
            }
            // MergeRadiance[f, 0] is passed directly to FluenceSum - no copy needed
        }

        Compose();
        device.SetRenderTargets(originalTargets);

        Gizmos.ClearSection("HRC");
        Gizmos.AddSectionString("HRC", $"Debug: {DebugNames[DebugIndex]} (F3)");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        var device = Renderer.Device;
        var size = CascadeSizes[0];

        // Set MRT: COLOR0 = Radiance, COLOR1 = Transmit
        MRT2[0] = VraysRadiance[frustum, 0];
        MRT2[1] = VraysTransmit[frustum, 0];
        device.SetRenderTargets(MRT2);
        device.Clear(Color.Black);

        FrustumSeedShader.Parameters["Emissivity"]?.SetValue(emissive);
        FrustumSeedShader.Parameters["Absorption"]?.SetValue(absorption);
        FrustumSeedShader.Parameters["WorldSize"]?.SetValue(WorldSize);
        FrustumSeedShader.Parameters["CascadeSize"]?.SetValue(size);
        FrustumSeedShader.Parameters["FrustumIndex"]?.SetValue((float)frustum);

        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, FrustumSeedShader);
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        ShaderBatch.End();
    }

    private void RenderExtensions(int frustum, int cascade)
    {
        var device = Renderer.Device;
        var size = CascadeSizes[cascade];
        var prevSize = CascadeSizes[cascade - 1];

        // Set MRT
        MRT2[0] = VraysRadiance[frustum, cascade];
        MRT2[1] = VraysTransmit[frustum, cascade];
        device.SetRenderTargets(MRT2);
        device.Clear(Color.Black);

        ExtensionsShader.Parameters["PrevSize"]?.SetValue(prevSize);
        ExtensionsShader.Parameters["CascadeSize"]?.SetValue(size);
        ExtensionsShader.Parameters["CascadeIndex"]?.SetValue(new Vector2(cascade, CascadeCount));
        ExtensionsShader.Parameters["PrevRadiance"]?.SetValue(VraysRadiance[frustum, cascade - 1]);
        ExtensionsShader.Parameters["PrevTransmit"]?.SetValue(VraysTransmit[frustum, cascade - 1]);

        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, ExtensionsShader);
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        ShaderBatch.End();
    }

    private void RenderMerging(int frustum, int cascade)
    {
        var device = Renderer.Device;
        var mergeSize = MergeSizes[cascade];
        var vraysSize = CascadeSizes[cascade];

        int nextCascade = cascade + 1;

        Texture2D prevMergeR = (nextCascade < CascadeCount) ? MergeRadiance[frustum, nextCascade] : BlackTexture;
        Texture2D prevMergeT = (nextCascade < CascadeCount) ? MergeTransmit[frustum, nextCascade] : WhiteTexture;
        Vector2 prevMergeSize = (nextCascade < CascadeCount) ? MergeSizes[nextCascade] : Vector2.One;

        // Set MRT
        MRT2[0] = MergeRadiance[frustum, cascade];
        MRT2[1] = MergeTransmit[frustum, cascade];
        device.SetRenderTargets(MRT2);
        device.Clear(Color.Black);

        MergingConesShader.Parameters["VraysRadiance"]?.SetValue(VraysRadiance[frustum, cascade]);
        MergingConesShader.Parameters["VraysTransmit"]?.SetValue(VraysTransmit[frustum, cascade]);
        MergingConesShader.Parameters["PrevRadiance"]?.SetValue(prevMergeR);
        MergingConesShader.Parameters["PrevTransmit"]?.SetValue(prevMergeT);
        MergingConesShader.Parameters["VraysSize"]?.SetValue(vraysSize);
        MergingConesShader.Parameters["PrevSize"]?.SetValue(prevMergeSize);
        MergingConesShader.Parameters["CascadeSize"]?.SetValue(mergeSize);
        MergingConesShader.Parameters["CascadeIndex"]?.SetValue(new Vector2(cascade, CascadeCount));

        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, MergingConesShader);
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;
        device.SamplerStates[3] = SamplerState.PointClamp;
        device.SamplerStates[4] = SamplerState.PointClamp;
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)mergeSize.X, (int)mergeSize.Y), Color.White);
        ShaderBatch.End();
    }

    private void Compose()
    {
        var device = Renderer.Device;
        device.SetRenderTarget(FinalTexture);
        device.Clear(Color.Black);

        // Pass MergeRadiance[f, 0] directly - skip FrustumOutput intermediate
        FluenceSumShader.Parameters["FrustumIndex0"]?.SetValue(MergeRadiance[0, 0]);
        FluenceSumShader.Parameters["FrustumIndex1"]?.SetValue(MergeRadiance[1, 0]);
        FluenceSumShader.Parameters["FrustumIndex2"]?.SetValue(MergeRadiance[2, 0]);
        FluenceSumShader.Parameters["FrustumIndex3"]?.SetValue(MergeRadiance[3, 0]);
        FluenceSumShader.Parameters["WorldSize"]?.SetValue(WorldSize);

        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, FluenceSumShader);
        device.SamplerStates[1] = SamplerState.PointClamp;
        device.SamplerStates[2] = SamplerState.PointClamp;
        device.SamplerStates[3] = SamplerState.PointClamp;
        device.SamplerStates[4] = SamplerState.PointClamp;
        ShaderBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)WorldSize.X, (int)WorldSize.Y), Color.White);
        ShaderBatch.End();
    }

    private void CopyTexture(RenderTarget2D source, RenderTarget2D destination)
    {
        var device = Renderer.Device;
        device.SetRenderTarget(destination);
        device.Clear(Color.Transparent);
        ShaderBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        ShaderBatch.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
        ShaderBatch.End();
    }

    private void HandleDebugInput()
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F3) && !PrevKeyState.IsKeyDown(Keys.F3))
            DebugIndex = (DebugIndex + 1) % DebugNames.Length;
        PrevKeyState = keyboard;
    }

    private void BuildDebugNames()
    {
        var names = new System.Collections.Generic.List<string> { "Final" };

        for (int f = 0; f < FrustumCount; f++)
        {
            for (int c = 0; c < CascadeCount; c++) names.Add($"VraysR F{f} C{c}");
            for (int c = 0; c < CascadeCount; c++) names.Add($"VraysT F{f} C{c}");
            for (int c = 0; c < CascadeCount; c++) names.Add($"MergeR F{f} C{c}");
            for (int c = 0; c < CascadeCount; c++) names.Add($"MergeT F{f} C{c}");
        }

        for (int f = 0; f < FrustumCount; f++) names.Add($"Frustum {f} Output");
        names.Add("Emissive Input");
        names.Add("Absorption Input");
        DebugNames = names.ToArray();
    }

    public override void Render()
    {
        Texture2D texture = GetDebugTexture();
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
        Renderer.SpriteBatch.Draw(texture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    private Texture2D GetDebugTexture()
    {
        if (DebugIndex == 0) return FinalTexture;
        int idx = DebugIndex - 1;

        int texturesPerFrustum = CascadeCount * 4;

        if (idx < FrustumCount * texturesPerFrustum)
        {
            int f = idx / texturesPerFrustum;
            int localIdx = idx % texturesPerFrustum;

            if (localIdx < CascadeCount)
                return VraysRadiance[f, localIdx];
            localIdx -= CascadeCount;

            if (localIdx < CascadeCount)
                return VraysTransmit[f, localIdx];
            localIdx -= CascadeCount;

            if (localIdx < CascadeCount)
                return MergeRadiance[f, localIdx];
            localIdx -= CascadeCount;

            return MergeTransmit[f, localIdx];
        }
        idx -= FrustumCount * texturesPerFrustum;

        if (idx < FrustumCount) return FrustumOutput[idx];
        idx -= FrustumCount;

        if (idx == 0) return SDFSystem.GetEmissiveTexture();
        return SDFSystem.GetAbsorptionTexture();
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void Dispose()
    {
        FinalTexture?.Dispose();
        BlackTexture?.Dispose();
        WhiteTexture?.Dispose();

        for (int f = 0; f < FrustumCount; f++)
        {
            FrustumOutput[f]?.Dispose();
            for (int c = 0; c < CascadeCount; c++)
            {
                VraysRadiance[f, c]?.Dispose();
                VraysTransmit[f, c]?.Dispose();
                MergeRadiance[f, c]?.Dispose();
                MergeTransmit[f, c]?.Dispose();
            }
        }
    }
}
