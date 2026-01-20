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
/// Single set of cascade surfaces reused for each frustum (matches GameMaker implementation).
/// </summary>
public class HRCGI : core.System
{
    private const int FrustumCount = 4;
    private const int ProbeScale = 2;  // 1 = full resolution, 2 = half probes
    private int CascadeCount;

    private SceneGeometry SDFSystem;
    private GizmosRenderer Gizmos;

    // Single set of cascade surfaces - reused for each frustum (like GameMaker)
    // RGB = radiance, A = transmittance (single-channel)
    private RenderTarget2D[] VraysCascade;
    private RenderTarget2D[] MergeCascade;

    // Per-frustum output (4 total) + final composited result
    private RenderTarget2D[] FrustumOutput;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    // Precomputed frustum transforms: 2x2 matrix as Vector4(m00, m01, m10, m11)
    // frustum 0: probe            -> (1,0,0,1) + (0,0)
    // frustum 1: 1.0 - probe.yx   -> (0,-1,-1,0) + (1,1)
    // frustum 2: 1.0 - probe      -> (-1,0,0,-1) + (1,1)
    // frustum 3: probe.yx         -> (0,1,1,0) + (0,0)
    private static readonly Vector4[] FrustumMatrices = {
        new Vector4(1, 0, 0, 1),    // identity
        new Vector4(0, -1, -1, 0),  // swap & negate
        new Vector4(-1, 0, 0, -1),  // negate both
        new Vector4(0, 1, 1, 0)     // swap
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
        SDFSystem = Scene.ECS.GetSystem<SceneGeometry>();
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();

        WorldSize = new Vector2(Renderer.ScaledHigherPowerOfTwo, Renderer.ScaledHigherPowerOfTwo);

        CalculateCascadeSizes();
        CreateRenderTargets();

        // Debug: vrays + merge per cascade + frustum outputs + emissive/absorption
        DebugTextureCount = 1 + CascadeCount * 2 + FrustumCount + 2;

        PrevKeyState = Keyboard.GetState();

        // Subscribe to render scale changes
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
        CascadeCount = (int)MathF.Ceiling(MathF.Log2(WorldSize.X));
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
        var format = SurfaceFormat.HalfVector4;

        // Single set of cascade surfaces (reused for each frustum)
        // RGB = radiance, A = transmittance (single-channel)
        VraysCascade = new RenderTarget2D[CascadeCount];
        MergeCascade = new RenderTarget2D[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            VraysCascade[cascade] = new RenderTarget2D(Renderer.Device, (int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, false, format, DepthFormat.None);
            MergeCascade[cascade] = new RenderTarget2D(Renderer.Device, (int)MergeSizes[cascade].X, (int)MergeSizes[cascade].Y, false, format, DepthFormat.None);
        }

        // Per-frustum output surfaces (half height to match cascade probes)
        FrustumOutput = new RenderTarget2D[FrustumCount];
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumOutput[frustum] = new RenderTarget2D(Renderer.Device, (int)WorldSize.X, (int)(WorldSize.Y / ProbeScale), false, format, DepthFormat.None);
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
            // Seed cascade 0 for this frustum
            RenderFrustumSeed(frustum, emissive, absorption);

            // Extend rays through cascades
            for (int cascade = 1; cascade < CascadeCount; cascade++)
                RenderExtensions(cascade);

            // Merge cascades back down
            for (int cascade = CascadeCount - 1; cascade >= 0; cascade--)
                RenderMerging(cascade);

            // Copy merge result to frustum output
            CopyToFrustumOutput(frustum);
        }

        Compose();
        Renderer.Device.SetRenderTargets(originalTargets);

        Gizmos.Set("HRCGI", $"World: {(int)WorldSize.X} | Cascades: {CascadeCount} | Frustums: {FrustumCount}");
    }

    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FrustumSeed")
            .Configure((0, SamplerState.PointClamp), (1, SamplerState.PointClamp))
            .SetTarget(VraysCascade[0])
            .Clear(Color.Black)
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
            .Clear(Color.Black)
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

        // Default for no next cascade: RGB=0 (no radiance), A=1 (full transmittance)
        // Using White which is (1,1,1,1) - the RGB doesn't matter since it will be multiplied by transmittance
        Renderer
            .Reset()
            .SetShader("HRC/HRC_MergingCones")
            .Configure(
                (0, SamplerState.LinearClamp),
                (1, SamplerState.LinearClamp))
            .SetTarget(MergeCascade[cascade])
            .Clear(Color.Black)
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
        Renderer.Device.Clear(Color.Black);
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        Renderer.SpriteBatch.Draw(MergeCascade[0], Vector2.Zero, Color.White);
        Renderer.SpriteBatch.End();
    }

    private void Compose()
    {
        // GameMaker uses gpu_set_tex_filter(false) = PointClamp for all HRC passes
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

        // Cascade textures (Vrays, Merge for each cascade)
        if (textureIndex < CascadeCount * 2)
        {
            int cascade = textureIndex / 2;
            int type = textureIndex % 2;
            return type switch
            {
                0 => VraysCascade[cascade],
                _ => MergeCascade[cascade]
            };
        }
        textureIndex -= CascadeCount * 2;

        // Frustum outputs
        if (textureIndex < FrustumCount) return FrustumOutput[textureIndex];
        textureIndex -= FrustumCount;

        // Scene textures
        if (textureIndex == 0) return SDFSystem.EmissiveTexture;
        return SDFSystem.AbsorptionTexture;
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void OnResize()
    {
        // Lazy resize - only rebuild if scaled power-of-two size actually changed
        int newSize = Renderer.ScaledHigherPowerOfTwo;
        if ((int)WorldSize.X == newSize)
            return;

        // Dispose existing render targets
        DisposeRenderTargets();

        // Recalculate sizes
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
            for (int cascade = 0; cascade < VraysCascade.Length; cascade++)
            {
                VraysCascade[cascade]?.Dispose();
                MergeCascade[cascade]?.Dispose();
            }
            VraysCascade = null;
            MergeCascade = null;
        }

        if (FrustumOutput != null)
        {
            for (int frustum = 0; frustum < FrustumCount; frustum++)
                FrustumOutput[frustum]?.Dispose();
            FrustumOutput = null;
        }
    }

    public override void Dispose()
    {
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;

        FinalTexture?.Dispose();
        FinalTexture = null;

        if (VraysCascade != null)
        {
            for (int cascade = 0; cascade < CascadeCount; cascade++)
            {
                VraysCascade[cascade]?.Dispose();
                MergeCascade[cascade]?.Dispose();
            }
            VraysCascade = null;
            MergeCascade = null;
        }

        if (FrustumOutput != null)
        {
            for (int frustum = 0; frustum < FrustumCount; frustum++)
            {
                FrustumOutput[frustum]?.Dispose();
            }
            FrustumOutput = null;
        }
    }
}
