using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

    // Solid textures for default values
    private Texture2D BlackTexture;
    private Texture2D WhiteTexture;

    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();

        WorldSize = new Vector2(Renderer.ScaledHigherPowerOfTwo, Renderer.ScaledHigherPowerOfTwo);

        // Create solid textures for defaults
        BlackTexture = Renderer.GetSolidTexture(Color.Black);
        WhiteTexture = Renderer.GetSolidTexture(Color.White);

        CalculateCascadeSizes();
        CreateRenderTargets();


        Renderer.RenderScaleChanged += OnRenderScaleChanged;

        Inspector.CreateWindow("hrcgi", "HRCGI");
        Inspector.AddLabel("hrcgi", "info", "...");
        Inspector.AddButton("hrcgi", "cycleQuality", "Cycle Quality", () =>
        {
            ProbeScaleIndex = (ProbeScaleIndex + 1) % ProbeScales.Length;
            DisposeRenderTargets();
            CalculateCascadeSizes();
            CreateRenderTargets();
        });
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
        var finalFormat = SurfaceFormat.HalfVector4;

        // Paired cascade surfaces (reused for each frustum)
        VraysRadiance = new RenderTarget2D[CascadeCount];
        VraysTransmittance = new RenderTarget2D[CascadeCount];
        MergeRadiance = new RenderTarget2D[CascadeCount];
        MergeTransmittance = new RenderTarget2D[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            VraysRadiance[cascade] = Renderer.CreateRenderTarget((int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, cascadeFormat);
            VraysTransmittance[cascade] = Renderer.CreateRenderTarget((int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, cascadeFormat);
            MergeRadiance[cascade] = Renderer.CreateRenderTarget((int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, cascadeFormat);
            MergeTransmittance[cascade] = Renderer.CreateRenderTarget((int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, cascadeFormat);
        }

        FrustumRadiance = new RenderTarget2D[FrustumCount];
        FrustumTransmittance = new RenderTarget2D[FrustumCount];
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumRadiance[frustum] = Renderer.CreateRenderTarget((int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), cascadeFormat);
            FrustumTransmittance[frustum] = Renderer.CreateRenderTarget((int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), cascadeFormat);
        }

        FinalTexture = Renderer.CreateRenderTarget((int)WorldSize.X, (int)WorldSize.Y, finalFormat);
    }

    public override void Update()
    {
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

        Inspector.SetLabel("hrcgi", "info", $"World: {(int)WorldSize.X} | Cascades: {CascadeCount} | Quality: {QualityNames[ProbeScaleIndex]}");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        // MRT: output to both radiance and transmittance targets
        Renderer
            .SetTargets(VraysRadiance[0], VraysTransmittance[0])
            .Clear(Color.Transparent)
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
        Renderer
            .SetTargets(VraysRadiance[cascade], VraysTransmittance[cascade])
            .Clear(Color.Transparent)
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
        Renderer
            .SetTargets(MergeRadiance[cascade], MergeTransmittance[cascade])
            .Clear(Color.Transparent)
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
        Renderer.Blit(MergeRadiance[0], FrustumRadiance[frustum]);
        Renderer.Blit(MergeTransmittance[0], FrustumTransmittance[frustum]);
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

    public override void Render()
    {
        Renderer.Blit(FinalTexture, BlendState.AlphaBlend, SamplerState.PointClamp);
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
