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
    private int CascadeCount;

    private SceneGeometry SDFSystem;
    private GizmosRenderer Gizmos;

    private RenderTarget2D[,] VraysRadiance;
    private RenderTarget2D[,] VraysTransmit;
    private RenderTarget2D[,] MergeRadiance;
    private RenderTarget2D[,] MergeTransmit;

    private RenderTargetBinding[] MRT2;

    private RenderTarget2D[] FrustumOutput;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    private KeyboardState PrevKeyState;
    private int DebugIndex = 0;
    private int DebugTextureCount;

    public override void Initialize()
    {
        SDFSystem = Scene.ECS.GetSystem<SceneGeometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        MRT2 = new RenderTargetBinding[2];

        WorldSize = new Vector2(Renderer.ScreenWidth, Renderer.ScreenHeight);

        CalculateCascadeSizes();
        CreateRenderTargets();

        DebugTextureCount = 1 + FrustumCount * CascadeCount * 4 + FrustumCount + 2;

        PrevKeyState = Keyboard.GetState();
    }

    private void CalculateCascadeSizes()
    {
        CascadeCount = (int)MathF.Ceiling(MathF.Log2(WorldSize.X));
        CascadeSizes = new Vector2[CascadeCount];
        MergeSizes = new Vector2[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            float interval = MathF.Pow(2, cascade);
            float virtualRays = interval + 1;
            int numProbes = (int)MathF.Floor(WorldSize.X / interval);

            CascadeSizes[cascade] = new Vector2(numProbes * virtualRays, WorldSize.Y);
            MergeSizes[cascade] = new Vector2(numProbes * interval, WorldSize.Y);
        }
    }

    private void CreateRenderTargets()
    {
        var format = SurfaceFormat.HalfVector4;

        VraysRadiance = new RenderTarget2D[FrustumCount, CascadeCount];
        VraysTransmit = new RenderTarget2D[FrustumCount, CascadeCount];
        MergeRadiance = new RenderTarget2D[FrustumCount, CascadeCount];
        MergeTransmit = new RenderTarget2D[FrustumCount, CascadeCount];
        FrustumOutput = new RenderTarget2D[FrustumCount];

        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                VraysRadiance[frustum, cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, format, DepthFormat.None);
                VraysTransmit[frustum, cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, format, DepthFormat.None);
                MergeRadiance[frustum, cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, format, DepthFormat.None);
                MergeTransmit[frustum, cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, format, DepthFormat.None);
            }
            FrustumOutput[frustum] = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
        }
        FinalTexture = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)WorldSize.Y, false, format, DepthFormat.None);
    }

    public override void Update()
    {
        HandleDebugInput();

        var emissive = SDFSystem.EmissiveTexture;
        var absorption = SDFSystem.AbsorptionTexture;
        var originalTargets = Renderer.Device.GetRenderTargets();

        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            RenderFrustumSeed(frustum, emissive, absorption);

            for (int cascade = 1; cascade < CascadeCount; cascade++)
                RenderExtensions(frustum, cascade);

            for (int cascade = CascadeCount - 1; cascade >= 0; cascade--)
                RenderMerging(frustum, cascade);
        }

        Compose();
        Renderer.Device.SetRenderTargets(originalTargets);

        Gizmos.Set("HRC", $"Debug: {DebugIndex} (F3)");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        var size = CascadeSizes[0];
        var shader = Renderer.GetShaderEffect("HRC/HRC_FrustumSeed");

        MRT2[0] = VraysRadiance[frustum, 0];
        MRT2[1] = VraysTransmit[frustum, 0];
        Renderer.Device.SetRenderTargets(MRT2);
        Renderer.Device.Clear(Color.Black);

        Renderer
            .SetParameter("Emissivity", emissive, shader)
            .SetParameter("Absorption", absorption, shader)
            .SetParameter("WorldSize", WorldSize, shader)
            .SetParameter("CascadeSize", size, shader)
            .SetParameter("FrustumIndex", (float)frustum, shader);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, shader);
        Renderer.Device.SamplerStates[1] = SamplerState.LinearClamp;
        Renderer.Device.SamplerStates[2] = SamplerState.LinearClamp;
        Renderer.SpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        Renderer.SpriteBatch.End();
    }

    private void RenderExtensions(int frustum, int cascade)
    {
        var size = CascadeSizes[cascade];
        var prevSize = CascadeSizes[cascade - 1];
        var shader = Renderer.GetShaderEffect("HRC/HRC_Extensions");

        MRT2[0] = VraysRadiance[frustum, cascade];
        MRT2[1] = VraysTransmit[frustum, cascade];
        Renderer.Device.SetRenderTargets(MRT2);
        Renderer.Device.Clear(Color.Black);

        Renderer
            .SetParameter("PrevSize", prevSize, shader)
            .SetParameter("CascadeSize", size, shader)
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount), shader)
            .SetParameter("PrevRadiance", VraysRadiance[frustum, cascade - 1], shader)
            .SetParameter("PrevTransmit", VraysTransmit[frustum, cascade - 1], shader);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, shader);
        Renderer.Device.SamplerStates[1] = SamplerState.PointClamp;
        Renderer.Device.SamplerStates[2] = SamplerState.PointClamp;
        Renderer.SpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(0, 0, (int)size.X, (int)size.Y), Color.White);
        Renderer.SpriteBatch.End();
    }

    private void RenderMerging(int frustum, int cascade)
    {
        var mergeSize = MergeSizes[cascade];
        var shader = Renderer.GetShaderEffect("HRC/HRC_MergingCones");
        int nextCascade = cascade + 1;

        Texture2D prevMergeR = (nextCascade < CascadeCount) ? MergeRadiance[frustum, nextCascade] : Renderer.GetSolidTexture(Color.Black);
        Texture2D prevMergeT = (nextCascade < CascadeCount) ? MergeTransmit[frustum, nextCascade] : Renderer.GetSolidTexture(Color.White);
        Vector2 prevMergeSize = (nextCascade < CascadeCount) ? MergeSizes[nextCascade] : Vector2.One;

        MRT2[0] = MergeRadiance[frustum, cascade];
        MRT2[1] = MergeTransmit[frustum, cascade];
        Renderer.Device.SetRenderTargets(MRT2);
        Renderer.Device.Clear(Color.Black);

        Renderer
            .SetParameter("VraysRadiance", VraysRadiance[frustum, cascade], shader)
            .SetParameter("VraysTransmit", VraysTransmit[frustum, cascade], shader)
            .SetParameter("PrevRadiance", prevMergeR, shader)
            .SetParameter("PrevTransmit", prevMergeT, shader)
            .SetParameter("VraysSize", CascadeSizes[cascade], shader)
            .SetParameter("PrevSize", prevMergeSize, shader)
            .SetParameter("CascadeSize", mergeSize, shader)
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount), shader);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, shader);
        Renderer.Device.SamplerStates[1] = SamplerState.PointClamp;
        Renderer.Device.SamplerStates[2] = SamplerState.PointClamp;
        Renderer.Device.SamplerStates[3] = SamplerState.PointClamp;
        Renderer.Device.SamplerStates[4] = SamplerState.PointClamp;
        Renderer.SpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(0, 0, (int)mergeSize.X, (int)mergeSize.Y), Color.White);
        Renderer.SpriteBatch.End();
    }

    private void Compose()
    {
        var shader = Renderer.GetShaderEffect("HRC/HRC_FluenceSum");

        Renderer.Device.SetRenderTarget(FinalTexture);
        Renderer.Device.Clear(Color.Black);

        Renderer
            .SetParameter("FrustumIndex0", MergeRadiance[0, 0], shader)
            .SetParameter("FrustumIndex1", MergeRadiance[1, 0], shader)
            .SetParameter("FrustumIndex2", MergeRadiance[2, 0], shader)
            .SetParameter("FrustumIndex3", MergeRadiance[3, 0], shader)
            .SetParameter("WorldSize", WorldSize, shader);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, shader);
        Renderer.Device.SamplerStates[1] = SamplerState.LinearClamp;
        Renderer.Device.SamplerStates[2] = SamplerState.LinearClamp;
        Renderer.Device.SamplerStates[3] = SamplerState.LinearClamp;
        Renderer.Device.SamplerStates[4] = SamplerState.LinearClamp;
        Renderer.SpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(0, 0, (int)WorldSize.X, (int)WorldSize.Y), Color.White);
        Renderer.SpriteBatch.End();
    }

    private void HandleDebugInput()
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F3) && !PrevKeyState.IsKeyDown(Keys.F3))
            DebugIndex = (DebugIndex + 1) % DebugTextureCount;
        PrevKeyState = keyboard;
    }

    public override void Render()
    {
        Texture2D texture = GetDebugTexture();
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);

        if (DebugIndex == 0)
        {
            Renderer.SpriteBatch.Draw(texture, Renderer.Device.Viewport.Bounds, Color.White);
        }
        else
        {
            float scale = MathF.Min(
                (float)Renderer.Device.Viewport.Width / texture.Width,
                (float)Renderer.Device.Viewport.Height / texture.Height
            );
            int drawWidth = (int)(texture.Width * scale);
            int drawHeight = (int)(texture.Height * scale);
            Renderer.SpriteBatch.Draw(texture, new Rectangle(0, 0, drawWidth, drawHeight), Color.White);
        }

        Renderer.SpriteBatch.End();
    }

    private Texture2D GetDebugTexture()
    {
        if (DebugIndex == 0) return FinalTexture;

        int textureIndex = DebugIndex - 1;
        int texturesPerFrustum = CascadeCount * 4;

        if (textureIndex < FrustumCount * texturesPerFrustum)
        {
            int frustum = textureIndex / texturesPerFrustum;
            int cascadeOffset = textureIndex % texturesPerFrustum;

            if (cascadeOffset < CascadeCount)
                return VraysRadiance[frustum, cascadeOffset];
            cascadeOffset -= CascadeCount;

            if (cascadeOffset < CascadeCount)
                return VraysTransmit[frustum, cascadeOffset];
            cascadeOffset -= CascadeCount;

            if (cascadeOffset < CascadeCount)
                return MergeRadiance[frustum, cascadeOffset];
            cascadeOffset -= CascadeCount;

            return MergeTransmit[frustum, cascadeOffset];
        }
        textureIndex -= FrustumCount * texturesPerFrustum;

        if (textureIndex < FrustumCount) return FrustumOutput[textureIndex];
        textureIndex -= FrustumCount;

        if (textureIndex == 0) return SDFSystem.EmissiveTexture;
        return SDFSystem.AbsorptionTexture;
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void Dispose()
    {
        FinalTexture?.Dispose();

        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumOutput[frustum]?.Dispose();
            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                VraysRadiance[frustum, cascade]?.Dispose();
                VraysTransmit[frustum, cascade]?.Dispose();
                MergeRadiance[frustum, cascade]?.Dispose();
                MergeTransmit[frustum, cascade]?.Dispose();
            }
        }
    }
}
