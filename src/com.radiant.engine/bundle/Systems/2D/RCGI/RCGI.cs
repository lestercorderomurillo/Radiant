using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class RCGI : core.System
{
    private const float CascadeLinear = 1.0f;
    private const float CascadeInterval = 4.0f;
    private const int MaxCascades = 8;

    private Effect RCShader;
    private SpriteBatch ShaderSpriteBatch;
    private Texture2D PixelTexture;
    private SceneGeometry SDFSystem;
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

    private EffectParameter _pScreenSize, _pCascadeSize, _pCascadeIndex, _pCascadeCount;
    private EffectParameter _pAngularPerAxis, _pProbeExtent, _pProbeSpacing;
    private EffectParameter _pRayOffset, _pRayRange, _pSDFScale, _pThetaScalar;
    private EffectParameter _pHigherAngularPerAxis, _pHigherExtent, _pInvProbeExtent;
    private EffectParameter _pInvScreenSize, _pInvCascadeSize;
    private EffectParameter _pPreCalcSkyColor, _pPreCalcSunColor, _pPreCalcSunAngle;
    private EffectParameter _pPreCalcSSunS, _pPreCalcISSunS, _pEnableSkyRadiance;
    private EffectParameter _pEmissiveTexture, _pSceneSDFTexture, _pCascadeTexture;

    public override void Initialize()
    {
        base.Initialize();

        RCShader = RenderPipeline.Window.Content.Load<Effect>("shaders/RCGI");
        ShaderSpriteBatch = new SpriteBatch(RenderPipeline.GraphicsDevice);
        PixelTexture = new Texture2D(RenderPipeline.GraphicsDevice, 1, 1);
        PixelTexture.SetData([Color.White]);

        SDFSystem = Scene.ECS.GetSystem<SceneGeometry>();

        ScreenSize = new Vector2(
            RenderPipeline.GraphicsDevice.Viewport.Width,
            RenderPipeline.GraphicsDevice.Viewport.Height
        );
        CascadeSize = ScreenSize / CascadeLinear;
        InvScreenSize = new Vector2(1f / ScreenSize.X, 1f / ScreenSize.Y);
        InvCascadeSize = new Vector2(1f / CascadeSize.X, 1f / CascadeSize.Y);

        CalculateActiveCascades();
        PreCalculateCascadeParameters();
        InitializeRenderTargets();
        CacheShaderParameters();

        CachedRasterizerState = new RasterizerState
        {
            MultiSampleAntiAlias = false,
            CullMode = CullMode.None
        };
    }

    private void CacheShaderParameters()
    {
        var p = RCShader.Parameters;
        _pScreenSize = p["ScreenSize"];
        _pCascadeSize = p["CascadeSize"];
        _pCascadeIndex = p["CascadeIndex"];
        _pCascadeCount = p["CascadeCount"];
        _pAngularPerAxis = p["AngularPerAxis"];
        _pProbeExtent = p["ProbeExtent"];
        _pProbeSpacing = p["ProbeSpacing"];
        _pRayOffset = p["RayOffset"];
        _pRayRange = p["RayRange"];
        _pSDFScale = p["SDFScale"];
        _pThetaScalar = p["ThetaScalar"];
        _pHigherAngularPerAxis = p["HigherAngularPerAxis"];
        _pHigherExtent = p["HigherExtent"];
        _pInvProbeExtent = p["InvProbeExtent"];
        _pInvScreenSize = p["InvScreenSize"];
        _pInvCascadeSize = p["InvCascadeSize"];
        _pPreCalcSkyColor = p["PreCalcSkyColor"];
        _pPreCalcSunColor = p["PreCalcSunColor"];
        _pPreCalcSunAngle = p["PreCalcSunAngle"];
        _pPreCalcSSunS = p["PreCalcSSunS"];
        _pPreCalcISSunS = p["PreCalcISSunS"];
        _pEnableSkyRadiance = p["EnableSkyRadiance"];
        _pEmissiveTexture = p["EmissiveTexture"];
        _pSceneSDFTexture = p["SceneSDFTexture"];
        _pCascadeTexture = p["CascadeTexture"];
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
        SurfaceFormat format = SurfaceFormat.HalfVector4;
        try
        {
            using var test = new RenderTarget2D(RenderPipeline.GraphicsDevice, 1, 1, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        }
        catch
        {
            format = SurfaceFormat.Color;
        }

        CascadeLayers = new RenderTarget2D[MaxCascades];
        for (int i = 0; i < MaxCascades; i++)
        {
            CascadeLayers[i] = new RenderTarget2D(
                RenderPipeline.GraphicsDevice,
                (int)CascadeSize.X, (int)CascadeSize.Y,
                false, format, DepthFormat.None
            );
        }

        FinalTexture = new RenderTarget2D(
            RenderPipeline.GraphicsDevice,
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
        var gd = RenderPipeline.GraphicsDevice;
        gd.SetRenderTarget(CascadeLayers[cascadeIndex]);
        gd.Clear(Color.Transparent);

        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.None;
        gd.RasterizerState = CachedRasterizerState;

        var emissive = SDFSystem.GetEmissiveTexture();
        var sdf = SDFSystem.GetSDFTexture();
        var cascade = cascadeIndex < ActiveCascades - 1 ? CascadeLayers[cascadeIndex + 1] : null;

        gd.Textures[1] = emissive;
        gd.Textures[2] = sdf;
        gd.Textures[3] = cascade;
        gd.SamplerStates[0] = SamplerState.LinearClamp; // SpriteBatch reserved
        gd.SamplerStates[1] = SamplerState.LinearClamp;
        gd.SamplerStates[2] = SamplerState.LinearClamp;
        gd.SamplerStates[3] = SamplerState.LinearClamp;

        ref var data = ref CascadeParameters[cascadeIndex];

        _pScreenSize.SetValue(ScreenSize);
        _pCascadeSize.SetValue(CascadeSize);
        _pCascadeIndex.SetValue((float)cascadeIndex);
        _pCascadeCount.SetValue((float)ActiveCascades);
        _pAngularPerAxis.SetValue(data.AngularPerAxis);
        _pProbeExtent.SetValue(data.ProbeExtent);
        _pProbeSpacing.SetValue(data.ProbeSpacing);
        _pRayOffset.SetValue(data.RayOffset);
        _pRayRange.SetValue(data.RayRange);
        _pSDFScale.SetValue(data.SDFScale);
        _pThetaScalar.SetValue(data.ThetaScalar);
        _pInvProbeExtent.SetValue(data.InvProbeExtent);
        _pInvScreenSize.SetValue(InvScreenSize);
        _pInvCascadeSize.SetValue(InvCascadeSize);
        _pPreCalcSkyColor.SetValue(CurrentSkyData.SkyColor);
        _pPreCalcSunColor.SetValue(CurrentSkyData.SunColor);
        _pPreCalcSunAngle.SetValue(CurrentSkyData.SunAngle);
        _pPreCalcSSunS.SetValue(CurrentSkyData.SSunS);
        _pPreCalcISSunS.SetValue(CurrentSkyData.ISSunS);
        _pEnableSkyRadiance.SetValue(EnableSkyRadiance);
        _pEmissiveTexture.SetValue(emissive);
        _pSceneSDFTexture.SetValue(sdf);
        _pCascadeTexture.SetValue(cascade);

        if (cascadeIndex < ActiveCascades - 1)
        {
            _pHigherAngularPerAxis?.SetValue(data.HigherAngularPerAxis);
            _pHigherExtent?.SetValue(data.HigherExtent);
        }

        RCShader.CurrentTechnique = RCShader.Techniques["GenerateOutputTexture"];
        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, RCShader);
        ShaderSpriteBatch.Draw(PixelTexture, new Rectangle(0, 0, (int)CascadeSize.X, (int)CascadeSize.Y), Color.White);
        ShaderSpriteBatch.End();

        gd.SetRenderTarget(null);
        gd.Textures[1] = null;
        gd.Textures[2] = null;
        gd.Textures[3] = null;
    }

    private void RenderFinal()
    {
        var gd = RenderPipeline.GraphicsDevice;
        gd.SetRenderTarget(FinalTexture);
        gd.Clear(Color.White);

        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        ShaderSpriteBatch.Draw(CascadeLayers[0], gd.Viewport.Bounds, Color.White);
        ShaderSpriteBatch.End();

        gd.SetRenderTarget(null);
    }

    public override void Render()
    {
        RenderPipeline.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        RenderPipeline.SpriteBatch.Draw(FinalTexture, RenderPipeline.GraphicsDevice.Viewport.Bounds, Color.White);
        RenderPipeline.SpriteBatch.End();
    }

    public override void Dispose()
    {
        RCShader?.Dispose();
        PixelTexture?.Dispose();
        ShaderSpriteBatch?.Dispose();
        FinalTexture?.Dispose();
        CachedRasterizerState?.Dispose();

        if (CascadeLayers != null)
        {
            foreach (var layer in CascadeLayers)
                layer?.Dispose();
        }
    }
}