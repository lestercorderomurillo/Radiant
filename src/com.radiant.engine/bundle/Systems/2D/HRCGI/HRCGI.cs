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
/// Uses single-channel transmittance packed into the alpha channel (RGB=radiance, A=transmittance).
/// Single set of cascade surfaces reused for each frustum (memory efficient).
/// </summary>
public class HRCGI : core.System
{
    private const int FrustumCount = 4;
    private const int MaxCascades = 11;
    private static readonly int[] ProbeScales = { 4, 3, 2, 1 };
    private static readonly string[] QualityNames = { "Performance", "Balanced", "Ultra", "Native" };
    private int ProbeScaleIndex = 3;
    private int ProbeScale => ProbeScales[ProbeScaleIndex];
    private int CascadeCount;

    private Geometry Geometry;
    private GizmosRenderer Gizmos;

    // Single set of cascade surfaces - reused for each frustum (memory efficient)
    private RenderTarget2D[] VraysCascade;
    private RenderTarget2D[] MergeCascade;

    // Per-frustum output + final composited result
    private RenderTarget2D[] FrustumOutput;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    // Precomputed frustum transforms
    private static readonly Vector4[] FrustumMatrices = {
        new Vector4(1, 0, 0, 1),
        new Vector4(0, -1, -1, 0),
        new Vector4(-1, 0, 0, -1),
        new Vector4(0, 1, 1, 0)
    };
    private static readonly Vector2[] FrustumOffsets = {
        new Vector2(0, 0),
        new Vector2(1, 1),
        new Vector2(1, 1),
        new Vector2(0, 0)
    };

    private KeyboardState PrevKeyState;
    private int DebugIndex = 0;
    private int DebugTextureCount;

    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        WorldSize = new Vector2(Renderer.ScaledHigherPowerOfTwo, Renderer.ScaledHigherPowerOfTwo);

        CalculateCascadeSizes();
        CreateRenderTargets();

        DebugTextureCount = 1 + CascadeCount * 2 + FrustumCount + 2;
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
        DebugTextureCount = 1 + CascadeCount * 2 + FrustumCount + 2;
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

        // Single set of cascade surfaces (reused for each frustum)
        VraysCascade = new RenderTarget2D[CascadeCount];
        MergeCascade = new RenderTarget2D[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            VraysCascade[cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
            MergeCascade[cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, cascadeFormat, DepthFormat.None);
        }

        FrustumOutput = new RenderTarget2D[FrustumCount];
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumOutput[frustum] = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), false, cascadeFormat, DepthFormat.None);
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
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FrustumSeed")
            .Configure((0, SamplerState.PointClamp), (1, SamplerState.PointClamp))
            .SetTarget(VraysCascade[0])
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
        Renderer
            .Reset()
            .SetShader("HRC/HRC_Extensions")
            .Configure((0, SamplerState.LinearClamp))
            .SetTarget(VraysCascade[cascade])
            .SetParameter("PrevCascade", VraysCascade[cascade - 1])
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

        Renderer
            .Reset()
            .SetShader("HRC/HRC_MergingCones")
            .Configure((0, SamplerState.LinearClamp), (1, SamplerState.LinearClamp))
            .SetTarget(MergeCascade[cascade])
            .SetParameter("VraysCascade", VraysCascade[cascade])
            .SetParameter("PrevCascade", hasNext ? MergeCascade[nextCascade] : Renderer.GetSolidTexture(Color.White))
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
        Renderer.Device.SetRenderTarget(FrustumOutput[frustum]);
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        Renderer.SpriteBatch.Draw(MergeCascade[0], Vector2.Zero, Color.White);
        Renderer.SpriteBatch.End();
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
            .SetParameter("FrustumIndex0", FrustumOutput[0])
            .SetParameter("FrustumIndex1", FrustumOutput[1])
            .SetParameter("FrustumIndex2", FrustumOutput[2])
            .SetParameter("FrustumIndex3", FrustumOutput[3])
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

        if (textureIndex < CascadeCount * 2)
        {
            int cascade = textureIndex / 2;
            int type = textureIndex % 2;
            return type == 0 ? VraysCascade[cascade] : MergeCascade[cascade];
        }
        textureIndex -= CascadeCount * 2;

        if (textureIndex < FrustumCount) return FrustumOutput[textureIndex];
        textureIndex -= FrustumCount;

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
        DebugTextureCount = 1 + CascadeCount * 2 + FrustumCount + 2;
    }

    private void DisposeRenderTargets()
    {
        FinalTexture?.Dispose();
        FinalTexture = null;

        if (VraysCascade != null)
        {
            for (int i = 0; i < VraysCascade.Length; i++)
            {
                VraysCascade[i]?.Dispose();
                MergeCascade[i]?.Dispose();
            }
            VraysCascade = null;
            MergeCascade = null;
        }

        if (FrustumOutput != null)
        {
            for (int i = 0; i < FrustumCount; i++)
                FrustumOutput[i]?.Dispose();
            FrustumOutput = null;
        }
    }

    public override void Dispose()
    {
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;
        DisposeRenderTargets();
    }
}
