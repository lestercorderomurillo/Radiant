using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class RCGI : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    private const float CascadeLinear = 1.0f;
    private const float CascadeInterval = 1.0f;
    private const int MaxCascades = 10;

    private Geometry Geometry;
    private RenderTarget2D[] CascadeLayers;
    private RenderTarget2D FinalTexture;
    private Vector2 ScreenSize;
    private Vector2 CascadeSize;
    private Vector2 InvScreenSize;
    private Vector2 InvCascadeSize;
    private int ActiveCascades;
    private CascadeData[] CascadeParameters;

    public float TimeOfDay = 0.25f;
    public bool EnableSkyRadiance = false;

    private struct CascadeData
    {
        public float AngularPerAxis;
        public Vector2 ProbeExtent;
        public Vector2 ProbeSpacing;
        public float RayOffset;
        public float RayRange;
        public float SDFScale;
        public float ThetaScalar;
        public float HigherAngularPerAxis;
        public Vector2 HigherExtent;
        public Vector2 InvProbeExtent;
    }

    private struct SkyData
    {
        public Vector3 SkyColor;
        public Vector3 SunColor;
        public float SunAngle;
        public float SSunS;
        public float ISSunS;
    }

    private SkyData CurrentSkyData;

    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();

        ScreenSize = Renderer.ScaledSize;
        CascadeSize = ScreenSize / CascadeLinear;
        InvScreenSize = new Vector2(1f / ScreenSize.X, 1f / ScreenSize.Y);
        InvCascadeSize = new Vector2(1f / CascadeSize.X, 1f / CascadeSize.Y);

        CalculateActiveCascades();
        PreCalculateCascadeParameters();
        InitializeRenderTargets();

        Renderer.RenderScaleChanged += OnRenderScaleChanged;

        if (Geometry != null)
            Geometry.EnableSDF = true;

        Inspector.CreateWindow("rcgi", "RCGI");
        Inspector.AddLabel("rcgi", "info", "...");
    }

    private void OnRenderScaleChanged(float newScale)
    {
        Vector2 newSize = Renderer.ScaledSize;
        if (ScreenSize == newSize)
            return;

        DisposeRenderTargets();

        ScreenSize = newSize;
        CascadeSize = ScreenSize / CascadeLinear;
        InvScreenSize = new Vector2(1f / ScreenSize.X, 1f / ScreenSize.Y);
        InvCascadeSize = new Vector2(1f / CascadeSize.X, 1f / CascadeSize.Y);

        CalculateActiveCascades();
        PreCalculateCascadeParameters();
        InitializeRenderTargets();
    }

    private void CalculateActiveCascades()
    {
        float diagonal = ScreenSize.Length();
        float currentReach = CascadeInterval;
        int count = 1;

        while (currentReach < diagonal && count < MaxCascades)
        {
            currentReach *= 4f;
            count++;
        }

        ActiveCascades = count;
    }

    private void PreCalculateCascadeParameters()
    {
        CascadeParameters = new CascadeData[MaxCascades];
        float screenDiagonal = ScreenSize.Length();

        for (int i = 0; i < MaxCascades; i++)
        {
            ref var data = ref CascadeParameters[i];

            float pow2i = MathF.Pow(2f, i);
            float pow4i = MathF.Pow(4f, i);

            data.AngularPerAxis = pow2i;
            float angularTotal = pow2i * pow2i * 4f;

            data.ProbeExtent = new Vector2(
                MathF.Floor(CascadeSize.X / pow2i),
                MathF.Floor(CascadeSize.Y / pow2i)
            );
            data.InvProbeExtent = Vector2.One / data.ProbeExtent;
            data.ProbeSpacing = new Vector2(CascadeLinear * pow2i);
            data.RayOffset = CascadeInterval * (1f - pow4i) / -3f;
            data.RayRange = CascadeInterval * pow4i + new Vector2(CascadeLinear * MathF.Pow(2f, i + 1f)).Length();
            data.SDFScale = screenDiagonal;
            data.ThetaScalar = MathF.Tau / angularTotal;

            if (i < MaxCascades - 1)
            {
                float nextPow2 = MathF.Pow(2f, i + 1);
                data.HigherAngularPerAxis = nextPow2;
                data.HigherExtent = new Vector2(
                    MathF.Floor(CascadeSize.X / nextPow2),
                    MathF.Floor(CascadeSize.Y / nextPow2)
                );
            }
        }
    }

    private void UpdateSkyParameters()
    {
        float sunHeight = MathF.Sin((TimeOfDay - 0.25f) * MathF.Tau);
        float dayFactor = Math.Clamp(sunHeight * 2f, 0f, 1f);
        float horizonFactor = 1f - Math.Clamp(MathF.Abs(sunHeight) * 4f, 0f, 1f);

        Vector3 skyColor = Vector3.Lerp(new Vector3(0.01f, 0.01f, 0.05f), new Vector3(0.3f, 0.6f, 1f), dayFactor);
        CurrentSkyData.SkyColor = Vector3.Lerp(skyColor, new Vector3(1f, 0.5f, 0.2f), horizonFactor * 0.7f);

        if (sunHeight < -0.1f)
        {
            CurrentSkyData.SunColor = Vector3.Zero;
        }
        else
        {
            float noonFactor = Math.Clamp(sunHeight * 3f, 0f, 1f);
            Vector3 sunColor = Vector3.Lerp(new Vector3(2f, 1f, 0.3f), new Vector3(3f, 2.8f, 2.5f), noonFactor);

            if (TimeOfDay > 0.5f)
                sunColor = Vector3.Lerp(new Vector3(3f, 2.8f, 2.5f), new Vector3(2.5f, 0.8f, 0.2f), Math.Clamp((TimeOfDay - 0.5f) * 4f, 0f, 1f));

            CurrentSkyData.SunColor = sunColor * Math.Clamp(sunHeight * 5f + 0.5f, 0f, 1f);
        }

        CurrentSkyData.SunAngle = (TimeOfDay - 0.5f) * MathF.PI;
        float sharpness = MathHelper.Lerp(16f, 64f, Math.Clamp(sunHeight * 2f, 0f, 1f));
        CurrentSkyData.SSunS = MathF.Sqrt(sharpness);
        CurrentSkyData.ISSunS = 1f / CurrentSkyData.SSunS;
    }

    private void InitializeRenderTargets()
    {
        var format = SurfaceFormat.HalfVector4;

        CascadeLayers = new RenderTarget2D[MaxCascades];
        for (int i = 0; i < MaxCascades; i++)
        {
            CascadeLayers[i] = Renderer.CreateRenderTarget(
                (int)CascadeSize.X, (int)CascadeSize.Y, format);
        }

        FinalTexture = Renderer.CreateRenderTarget(
            (int)ScreenSize.X, (int)ScreenSize.Y, format);
    }

    public override void Update()
    {
        UpdateSkyParameters();

        var emissive = Geometry.EmissiveTexture;
        var sdf = Geometry.SDFTexture;

        Renderer.PushTargets();

        for (int i = ActiveCascades - 1; i >= 0; i--)
            RenderCascade(i, emissive, sdf);

        RenderFinal();

        Renderer.PopTargets();

        Inspector.SetLabel("rcgi", "info", $"Screen: {(int)ScreenSize.X}x{(int)ScreenSize.Y} | Cascades: {ActiveCascades}");
    }

    private void RenderCascade(int cascadeIndex, Texture2D emissive, Texture2D sdf)
    {
        ref var data = ref CascadeParameters[cascadeIndex];
        bool hasHigher = cascadeIndex < ActiveCascades - 1;
        var higherCascade = hasHigher ? CascadeLayers[cascadeIndex + 1] : null;

        Renderer
            .Reset()
            .SetShader("RCGI/RCGI")
            .Configure(
                (0, SamplerState.LinearClamp),
                (1, SamplerState.LinearClamp),
                (2, SamplerState.LinearClamp))
            .SetTarget(CascadeLayers[cascadeIndex])
            .Clear(Color.Transparent)
            .SetParameter("EmissiveTexture", emissive)
            .SetParameter("SceneSDFTexture", sdf)
            .SetParameter("CascadeTexture", higherCascade)
            .SetParameter("ScreenSize", ScreenSize)
            .SetParameter("CascadeSize", CascadeSize)
            .SetParameter("ProbeExtent", data.ProbeExtent)
            .SetParameter("ProbeSpacing", data.ProbeSpacing)
            .SetParameter("HigherExtent", hasHigher ? data.HigherExtent : Vector2.Zero)
            .SetParameter("InvProbeExtent", data.InvProbeExtent)
            .SetParameter("InvScreenSize", InvScreenSize)
            .SetParameter("InvCascadeSize", InvCascadeSize)
            .SetParameter("CascadeIndex", (float)cascadeIndex)
            .SetParameter("CascadeCount", (float)ActiveCascades)
            .SetParameter("AngularPerAxis", data.AngularPerAxis)
            .SetParameter("RayOffset", data.RayOffset)
            .SetParameter("RayRange", data.RayRange)
            .SetParameter("SDFScale", data.SDFScale)
            .SetParameter("ThetaScalar", data.ThetaScalar)
            .SetParameter("HigherAngularPerAxis", hasHigher ? data.HigherAngularPerAxis : 0f)
            .SetParameter("PreCalcSkyColor", CurrentSkyData.SkyColor)
            .SetParameter("PreCalcSunAngle", CurrentSkyData.SunAngle)
            .SetParameter("PreCalcSunColor", CurrentSkyData.SunColor)
            .SetParameter("PreCalcSSunS", CurrentSkyData.SSunS)
            .SetParameter("PreCalcISSunS", CurrentSkyData.ISSunS)
            .SetParameter("EnableSkyRadiance", EnableSkyRadiance)
            .Draw()
            .Commit();
    }

    private void RenderFinal()
    {
        Renderer.SetTarget(FinalTexture).Clear(Color.Black);
        Renderer.Blit(CascadeLayers[0], BlendState.Opaque, SamplerState.LinearClamp);
    }

    public override void Render()
    {
        Renderer.Blit(FinalTexture, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScaledSize;
        if (ScreenSize == newSize)
            return;

        DisposeRenderTargets();

        ScreenSize = newSize;
        CascadeSize = ScreenSize / CascadeLinear;
        InvScreenSize = new Vector2(1f / ScreenSize.X, 1f / ScreenSize.Y);
        InvCascadeSize = new Vector2(1f / CascadeSize.X, 1f / CascadeSize.Y);

        CalculateActiveCascades();
        PreCalculateCascadeParameters();
        InitializeRenderTargets();
    }

    private void DisposeRenderTargets()
    {
        FinalTexture?.Dispose();
        FinalTexture = null;

        if (CascadeLayers != null)
        {
            for (int i = 0; i < CascadeLayers.Length; i++)
                CascadeLayers[i]?.Dispose();
            CascadeLayers = null;
        }
    }

    public override void Dispose()
    {
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;

        FinalTexture?.Dispose();
        FinalTexture = null;

        if (CascadeLayers != null)
        {
            for (int i = 0; i < CascadeLayers.Length; i++)
                CascadeLayers[i]?.Dispose();
            CascadeLayers = null;
        }
    }
}
