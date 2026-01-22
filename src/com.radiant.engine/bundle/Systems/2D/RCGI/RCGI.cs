using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class RCGI : core.System
{
    private const float CascadeLinear = 1.0f;
    private const float CascadeInterval = 1.0f;
    private const int MaxCascades = 8;

    private Effect RCShader;
    private SpriteBatch ShaderSpriteBatch;
    private Geometry GeometrySystem;
    private RenderTarget2D[] CascadeLayers;
    private RenderTarget2D FinalTexture;
    private Vector2 ScreenSize;
    private Vector2 CascadeSize;
    private Vector2 InvScreenSize;
    private Vector2 InvCascadeSize;
    private int ActiveCascades;
    private CascadeData[] CascadeParameters;
    private RasterizerState CachedRasterizerState;

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
        RCShader = Renderer.GetShaderEffect("RCGI/RCGI");
        ShaderSpriteBatch = Renderer.SpriteBatch;
        GeometrySystem = Scene.ECS.GetSystem<Geometry>();

        ScreenSize = Renderer.ScaledSize;
        CascadeSize = ScreenSize / CascadeLinear;
        InvScreenSize = new Vector2(1f / ScreenSize.X, 1f / ScreenSize.Y);
        InvCascadeSize = new Vector2(1f / CascadeSize.X, 1f / CascadeSize.Y);

        CalculateActiveCascades();
        PreCalculateCascadeParameters();
        InitializeRenderTargets();

        CachedRasterizerState = new RasterizerState
        {
            MultiSampleAntiAlias = false,
            CullMode = CullMode.None
        };

        // Subscribe to render scale changes
        Renderer.RenderScaleChanged += OnRenderScaleChanged;
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
        var device = Renderer.Device;

        SurfaceFormat format = SurfaceFormat.HalfVector4;
        try
        {
            using var test = new RenderTarget2D(device, 1, 1, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        }
        catch
        {
            format = SurfaceFormat.Color;
        }

        CascadeLayers = new RenderTarget2D[MaxCascades];
        for (int i = 0; i < MaxCascades; i++)
        {
            CascadeLayers[i] = new RenderTarget2D(
                device,
                (int)CascadeSize.X, (int)CascadeSize.Y,
                false, format, DepthFormat.None
            );
        }

        FinalTexture = new RenderTarget2D(
            device,
            (int)ScreenSize.X, (int)ScreenSize.Y,
            false, SurfaceFormat.Color, DepthFormat.None
        );
    }

    public override void Update()
    {
        UpdateSkyParameters();

        for (int i = ActiveCascades - 1; i >= 0; i--)
            RenderCascade(i);

        RenderFinal();
    }

    private void RenderCascade(int cascadeIndex)
    {
        var device = Renderer.Device;
        device.SetRenderTarget(CascadeLayers[cascadeIndex]);
        device.Clear(Color.Transparent);

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = CachedRasterizerState;

        var emissive = GeometrySystem.EmissiveTexture;
        var sdf = GeometrySystem.SDFTexture;
        var cascade = cascadeIndex < ActiveCascades - 1 ? CascadeLayers[cascadeIndex + 1] : null;

        device.Textures[1] = emissive;
        device.Textures[2] = sdf;
        device.Textures[3] = cascade;
        device.SamplerStates[0] = SamplerState.LinearClamp;
        device.SamplerStates[1] = SamplerState.LinearClamp;
        device.SamplerStates[2] = SamplerState.LinearClamp;
        device.SamplerStates[3] = SamplerState.LinearClamp;

        ref var data = ref CascadeParameters[cascadeIndex];

        Renderer.SetParameter(RCShader, "ScreenSize", ScreenSize);
        Renderer.SetParameter(RCShader, "CascadeSize", CascadeSize);
        Renderer.SetParameter(RCShader, "CascadeIndex", (float)cascadeIndex);
        Renderer.SetParameter(RCShader, "CascadeCount", (float)ActiveCascades);
        Renderer.SetParameter(RCShader, "AngularPerAxis", data.AngularPerAxis);
        Renderer.SetParameter(RCShader, "ProbeExtent", data.ProbeExtent);
        Renderer.SetParameter(RCShader, "ProbeSpacing", data.ProbeSpacing);
        Renderer.SetParameter(RCShader, "RayOffset", data.RayOffset);
        Renderer.SetParameter(RCShader, "RayRange", data.RayRange);
        Renderer.SetParameter(RCShader, "SDFScale", data.SDFScale);
        Renderer.SetParameter(RCShader, "ThetaScalar", data.ThetaScalar);
        Renderer.SetParameter(RCShader, "InvProbeExtent", data.InvProbeExtent);
        Renderer.SetParameter(RCShader, "InvScreenSize", InvScreenSize);
        Renderer.SetParameter(RCShader, "InvCascadeSize", InvCascadeSize);
        Renderer.SetParameter(RCShader, "PreCalcSkyColor", CurrentSkyData.SkyColor);
        Renderer.SetParameter(RCShader, "PreCalcSunColor", CurrentSkyData.SunColor);
        Renderer.SetParameter(RCShader, "PreCalcSunAngle", CurrentSkyData.SunAngle);
        Renderer.SetParameter(RCShader, "PreCalcSSunS", CurrentSkyData.SSunS);
        Renderer.SetParameter(RCShader, "PreCalcISSunS", CurrentSkyData.ISSunS);
        Renderer.SetParameter(RCShader, "EnableSkyRadiance", EnableSkyRadiance);
        Renderer.SetParameter(RCShader, "EmissiveTexture", emissive);
        Renderer.SetParameter(RCShader, "SceneSDFTexture", sdf);
        Renderer.SetParameter(RCShader, "CascadeTexture", cascade);

        if (cascadeIndex < ActiveCascades - 1)
        {
            Renderer.SetParameter(RCShader, "HigherAngularPerAxis", data.HigherAngularPerAxis);
            Renderer.SetParameter(RCShader, "HigherExtent", data.HigherExtent);
        }

        RCShader.CurrentTechnique = RCShader.Techniques["GenerateOutputTexture"];
        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, RCShader);
        ShaderSpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(0, 0, (int)CascadeSize.X, (int)CascadeSize.Y), Color.White);
        ShaderSpriteBatch.End();

        device.SetRenderTarget(null);
        device.Textures[1] = null;
        device.Textures[2] = null;
        device.Textures[3] = null;
    }

    private void RenderFinal()
    {
        var device = Renderer.Device;
        device.SetRenderTarget(FinalTexture);
        device.Clear(Color.White);

        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        ShaderSpriteBatch.Draw(CascadeLayers[0], device.Viewport.Bounds, Color.White);
        ShaderSpriteBatch.End();

        device.SetRenderTarget(null);
    }

    public override void Render()
    {
        Renderer.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        Renderer.SpriteBatch.Draw(FinalTexture, Renderer.Device.Viewport.Bounds, Color.White);
        Renderer.SpriteBatch.End();
    }

    public RenderTarget2D GetOutput() => FinalTexture;

    public override void OnResize()
    {
        // Lazy resize - only rebuild if scaled screen size actually changed
        Vector2 newSize = Renderer.ScaledSize;
        if (ScreenSize == newSize)
            return;

        // Dispose existing render targets
        DisposeRenderTargets();

        // Recalculate sizes
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
            foreach (var layer in CascadeLayers)
                layer?.Dispose();
            CascadeLayers = null;
        }
    }

    public override void Dispose()
    {
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;

        // Note: RCShader, ShaderSpriteBatch are managed by Renderer
        FinalTexture?.Dispose();
        FinalTexture = null;

        CachedRasterizerState?.Dispose();
        CachedRasterizerState = null;

        if (CascadeLayers != null)
        {
            foreach (var layer in CascadeLayers)
                layer?.Dispose();
            CascadeLayers = null;
        }
    }
}