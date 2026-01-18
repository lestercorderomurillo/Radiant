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
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FrustumSeed")
            .Configure((0, SamplerState.LinearClamp), (1, SamplerState.LinearClamp))
            .SetTargets(VraysRadiance[frustum, 0], VraysTransmit[frustum, 0])
            .Clear(Color.Black)
            .SetParameter("Emissivity", emissive)
            .SetParameter("Absorption", absorption)
            .SetParameter("WorldSize", WorldSize)
            .SetParameter("CascadeSize", CascadeSizes[0])
            .SetParameter("FrustumIndex", (float)frustum)
            .Draw()
            .Commit();
    }

    private void RenderExtensions(int frustum, int cascade)
    {
        Renderer
            .Reset()
            .SetShader("HRC/HRC_Extensions")
            .Configure((0, SamplerState.PointClamp), (1, SamplerState.PointClamp))
            .SetTargets(VraysRadiance[frustum, cascade], VraysTransmit[frustum, cascade])
            .Clear(Color.Black)
            .SetParameter("PrevSize", CascadeSizes[cascade - 1])
            .SetParameter("CascadeSize", CascadeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .SetParameter("PrevRadiance", VraysRadiance[frustum, cascade - 1])
            .SetParameter("PrevTransmit", VraysTransmit[frustum, cascade - 1])
            .Draw()
            .Commit();
    }

    private void RenderMerging(int frustum, int cascade)
    {
        int nextCascade = cascade + 1;
        bool hasNext = nextCascade < CascadeCount;

        Renderer
            .Reset()
            .SetShader("HRC/HRC_MergingCones")
            .Configure(
                (0, SamplerState.PointClamp),
                (1, SamplerState.PointClamp),
                (2, SamplerState.PointClamp),
                (3, SamplerState.PointClamp))
            .SetTargets(MergeRadiance[frustum, cascade], MergeTransmit[frustum, cascade])
            .Clear(Color.Black)
            .SetParameter("VraysRadiance", VraysRadiance[frustum, cascade])
            .SetParameter("VraysTransmit", VraysTransmit[frustum, cascade])
            .SetParameter("PrevRadiance", hasNext ? MergeRadiance[frustum, nextCascade] : Renderer.GetSolidTexture(Color.Black))
            .SetParameter("PrevTransmit", hasNext ? MergeTransmit[frustum, nextCascade] : Renderer.GetSolidTexture(Color.White))
            .SetParameter("VraysSize", CascadeSizes[cascade])
            .SetParameter("PrevSize", hasNext ? MergeSizes[nextCascade] : Vector2.One)
            .SetParameter("CascadeSize", MergeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .Draw()
            .Commit();
    }

    private void Compose()
    {
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FluenceSum")
            .Configure(
                (0, SamplerState.LinearClamp),
                (1, SamplerState.LinearClamp),
                (2, SamplerState.LinearClamp),
                (3, SamplerState.LinearClamp))
            .SetTarget(FinalTexture)
            .Clear(Color.Black)
            .SetParameter("FrustumIndex0", MergeRadiance[0, 0])
            .SetParameter("FrustumIndex1", MergeRadiance[1, 0])
            .SetParameter("FrustumIndex2", MergeRadiance[2, 0])
            .SetParameter("FrustumIndex3", MergeRadiance[3, 0])
            .SetParameter("WorldSize", WorldSize)
            .Draw()
            .Commit();
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
