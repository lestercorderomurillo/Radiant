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
/// Uses MRT with separate RGB textures for radiance and transmittance (stained glass support).
/// Single set of cascade surfaces reused for each frustum (memory efficient).
/// </summary>
public class HRCGI : core.System
{
    private const int FrustumCount = 4;
    private const int MaxCascades = 11;
    private static readonly int[] ProbeScales = [4, 3, 2, 1];
    private static readonly string[] QualityNames = ["Performance", "Balanced", "Ultra", "Native"];
    private int ProbeScaleIndex = 3;
    private int ProbeScale => ProbeScales[ProbeScaleIndex];
    private int CascadeCount;

    private Geometry Geometry;
    private GizmosRenderer Gizmos;

    // Paired cascade surfaces (radiance + transmittance) - reused for each frustum
    private RenderTarget2D[] VraysRadiance;
    private RenderTarget2D[] VraysTransmittance;
    private RenderTarget2D[] MergeRadiance;
    private RenderTarget2D[] MergeTransmittance;

    // Per-frustum output + final composited result
    private RenderTarget2D[] FrustumRadiance;
    private RenderTarget2D[] FrustumTransmittance;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    // Precomputed frustum transforms
    private static readonly Vector4[] FrustumMatrices = [
        new Vector4(1, 0, 0, 1),
        new Vector4(0, -1, -1, 0),
        new Vector4(-1, 0, 0, -1),
        new Vector4(0, 1, 1, 0)
    ];
    private static readonly Vector2[] FrustumOffsets = [
        new Vector2(0, 0),
        new Vector2(1, 1),
        new Vector2(1, 1),
        new Vector2(0, 0)
    ];

    private KeyboardState PrevKeyState;
    private int DebugIndex = 0;
    private int DebugTextureCount;

    // Solid textures for default values
    private Texture2D BlackTexture;
    private Texture2D WhiteTexture;

    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        WorldSize = new Vector2(Renderer.ScaledHigherPowerOfTwo, Renderer.ScaledHigherPowerOfTwo);

        // Create solid textures for defaults
        BlackTexture = Renderer.GetSolidTexture(Color.Black);
        WhiteTexture = Renderer.GetSolidTexture(Color.White);

        CalculateCascadeSizes();
        CreateRenderTargets();

        DebugTextureCount = 1 + CascadeCount * 4 + FrustumCount * 2 + 2;
        PrevKeyState = Keyboard.GetState();
        Renderer.RenderScaleChanged += OnRenderScaleChanged;
    }

    private void OnRenderScaleChanged(float newScale)
    {
        int newSize = Renderer.ScaledHigherPowerOfTwo;
        if ((int)WorldSize.X == newSize)
            return;

        DisposeRenderTargets();
        WorldSize = new Vector2(newSize, newSize);
        CalculateCascadeSizes();
        CreateRenderTargets();
        DebugTextureCount = 1 + CascadeCount * 4 + FrustumCount * 2 + 2;
    }

    private void CalculateCascadeSizes()
    {
        CascadeCount = Math.Min((int)MathF.Ceiling(MathF.Log2(WorldSize.X)), MaxCascades);
        CascadeSizes = new Vector2[CascadeCount];
        MergeSizes = new Vector2[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            float interval = MathF.Pow(2, cascade);
            float virtualRays = interval + 1;
            int numProbes = (int)MathF.Floor(WorldSize.X / interval);

            CascadeSizes[cascade] = new Vector2(numProbes * virtualRays, WorldSize.Y / ProbeScale);
            MergeSizes[cascade] = new Vector2(numProbes * interval, WorldSize.Y / ProbeScale);
        }
    }

    private void CreateRenderTargets()
    {
        var cascadeFormat = SurfaceFormat.HalfVector4;
        var finalFormat = SurfaceFormat.Color;

        // Paired cascade surfaces (reused for each frustum)
        VraysRadiance = new RenderTarget2D[CascadeCount];
        VraysTransmittance = new RenderTarget2D[CascadeCount];
        MergeRadiance = new RenderTarget2D[CascadeCount];
        MergeTransmittance = new RenderTarget2D[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            VraysRadiance[cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
            VraysTransmittance[cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
            MergeRadiance[cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
            MergeTransmittance[cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
        }

        FrustumRadiance = new RenderTarget2D[FrustumCount];
        FrustumTransmittance = new RenderTarget2D[FrustumCount];
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumRadiance[frustum] = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), false, cascadeFormat, DepthFormat.None);
            FrustumTransmittance[frustum] = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), false, cascadeFormat, DepthFormat.None);
        }

        FinalTexture = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)WorldSize.Y, false, finalFormat, DepthFormat.None);
    }

    public override void Update()
    {
        HandleDebugInput();

        var emissive = Geometry.EmissiveTexture;
        var absorption = Geometry.AbsorptionTexture;

        Renderer.PushTargets();

        // Process each frustum sequentially, reusing cascade textures
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            RenderFrustumSeed(frustum, emissive, absorption);

            for (int cascade = 1; cascade < CascadeCount; cascade++)
                RenderExtensions(cascade);

            for (int cascade = CascadeCount - 1; cascade >= 0; cascade--)
                RenderMerging(cascade);

            CopyToFrustumOutput(frustum);
        }

        Compose();

        Renderer.PopTargets();

        Gizmos.Set("HRCGI", $"World: {(int)WorldSize.X} | Cascades: {CascadeCount} | Shadow Quality: {QualityNames[ProbeScaleIndex]} [F5]");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        // MRT: output to both radiance and transmittance targets
        Renderer.Device.SetRenderTargets(VraysRadiance[0], VraysTransmittance[0]);
        Renderer.Device.Clear(Color.Transparent);

        Renderer
            .Reset()
            .SetShader("HRC/HRC_FrustumSeed")
            .Configure(SamplerState.PointClamp)
            .SetParameter("Emissivity", emissive)
            .SetParameter("Absorption", absorption)
            .SetParameter("WorldSize", WorldSize)
            .SetParameter("CascadeSize", CascadeSizes[0])
            .SetParameter("FrustumMatrix", FrustumMatrices[frustum])
            .SetParameter("FrustumOffset", FrustumOffsets[frustum])
            .SetParameter("ProbeScale", (float)ProbeScale)
            .Draw()
            .Commit();
    }

    private void RenderExtensions(int cascade)
    {
        // MRT: output to both radiance and transmittance targets
        Renderer.Device.SetRenderTargets(VraysRadiance[cascade], VraysTransmittance[cascade]);
        Renderer.Device.Clear(Color.Transparent);

        Renderer
            .Reset()
            .SetShader("HRC/HRC_Extensions")
            .Configure(SamplerState.LinearClamp)
            .SetParameter("PrevRadiance", VraysRadiance[cascade - 1])
            .SetParameter("PrevTransmittance", VraysTransmittance[cascade - 1])
            .SetParameter("PrevSize", CascadeSizes[cascade - 1])
            .SetParameter("CascadeSize", CascadeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .SetParameter("ProbeScale", (float)ProbeScale)
            .Draw()
            .Commit();
    }

    private void RenderMerging(int cascade)
    {
        int nextCascade = (cascade + 1) % CascadeCount;
        bool hasNext = cascade + 1 < CascadeCount;

        // MRT: output to both radiance and transmittance targets
        Renderer.Device.SetRenderTargets(MergeRadiance[cascade], MergeTransmittance[cascade]);
        Renderer.Device.Clear(Color.Transparent);

        Renderer
            .Reset()
            .SetShader("HRC/HRC_MergingCones")
            .Configure(SamplerState.LinearClamp)
            .SetParameter("VraysRadiance", VraysRadiance[cascade])
            .SetParameter("VraysTransmittance", VraysTransmittance[cascade])
            .SetParameter("PrevRadiance", hasNext ? MergeRadiance[nextCascade] : BlackTexture)
            .SetParameter("PrevTransmittance", hasNext ? MergeTransmittance[nextCascade] : WhiteTexture)
            .SetParameter("VraysSize", CascadeSizes[cascade])
            .SetParameter("PrevSize", hasNext ? MergeSizes[nextCascade] : Vector2.One)
            .SetParameter("CascadeSize", MergeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .SetParameter("ProbeScale", (float)ProbeScale)
            .Draw()
            .Commit();
    }

    private void CopyToFrustumOutput(int frustum)
    {
        // Copy radiance
        Renderer.Device.SetRenderTarget(FrustumRadiance[frustum]);
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        Renderer.SpriteBatch.Draw(MergeRadiance[0], Vector2.Zero, Color.White);
        Renderer.SpriteBatch.End();

        // Copy transmittance
        Renderer.Device.SetRenderTarget(FrustumTransmittance[frustum]);
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        Renderer.SpriteBatch.Draw(MergeTransmittance[0], Vector2.Zero, Color.White);
        Renderer.SpriteBatch.End();
    }

    private void Compose()
    {
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FluenceSum")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(FinalTexture)
            .SetParameter("FrustumIndex0", FrustumRadiance[0])
            .SetParameter("FrustumIndex1", FrustumRadiance[1])
            .SetParameter("FrustumIndex2", FrustumRadiance[2])
            .SetParameter("FrustumIndex3", FrustumRadiance[3])
            .SetParameter("WorldSize", WorldSize)
            .Draw()
            .Commit();
    }

    private void HandleDebugInput()
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F3) && !PrevKeyState.IsKeyDown(Keys.F3))
            DebugIndex = (DebugIndex + 1) % DebugTextureCount;
        if (keyboard.IsKeyDown(Keys.F5) && !PrevKeyState.IsKeyDown(Keys.F5))
        {
            ProbeScaleIndex = (ProbeScaleIndex + 1) % ProbeScales.Length;
            DisposeRenderTargets();
            CalculateCascadeSizes();
            CreateRenderTargets();
        }
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

        // Vrays (radiance + transmittance pairs)
        if (textureIndex < CascadeCount * 2)
        {
            int cascade = textureIndex / 2;
            int type = textureIndex % 2;
            return type == 0 ? VraysRadiance[cascade] : VraysTransmittance[cascade];
        }
        textureIndex -= CascadeCount * 2;

        // Merge (radiance + transmittance pairs)
        if (textureIndex < CascadeCount * 2)
        {
            int cascade = textureIndex / 2;
            int type = textureIndex % 2;
            return type == 0 ? MergeRadiance[cascade] : MergeTransmittance[cascade];
        }
        textureIndex -= CascadeCount * 2;

        // Frustum outputs
        if (textureIndex < FrustumCount * 2)
        {
            int frustum = textureIndex / 2;
            int type = textureIndex % 2;
            return type == 0 ? FrustumRadiance[frustum] : FrustumTransmittance[frustum];
        }
        textureIndex -= FrustumCount * 2;

        if (textureIndex == 0) return Geometry.EmissiveTexture;
        return Geometry.AbsorptionTexture;
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void OnResize()
    {
        int newSize = Renderer.ScaledHigherPowerOfTwo;
        if ((int)WorldSize.X == newSize)
            return;

        DisposeRenderTargets();
        WorldSize = new Vector2(newSize, newSize);
        CalculateCascadeSizes();
        CreateRenderTargets();
        DebugTextureCount = 1 + CascadeCount * 4 + FrustumCount * 2 + 2;
    }

    private void DisposeRenderTargets()
    {
        FinalTexture?.Dispose();
        FinalTexture = null;

        if (VraysRadiance != null)
        {
            for (int i = 0; i < VraysRadiance.Length; i++)
            {
                VraysRadiance[i]?.Dispose();
                VraysTransmittance[i]?.Dispose();
                MergeRadiance[i]?.Dispose();
                MergeTransmittance[i]?.Dispose();
            }
            VraysRadiance = null;
            VraysTransmittance = null;
            MergeRadiance = null;
            MergeTransmittance = null;
        }

        if (FrustumRadiance != null)
        {
            for (int i = 0; i < FrustumCount; i++)
            {
                FrustumRadiance[i]?.Dispose();
                FrustumTransmittance[i]?.Dispose();
            }
            FrustumRadiance = null;
            FrustumTransmittance = null;
        }
    }

    public override void Dispose()
    {
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;
        DisposeRenderTargets();
    }
}
