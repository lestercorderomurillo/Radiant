static const float PI = 3.14159265359;
static const float TAU = 6.28318530718;
static const float EPSILON = 0.00001;
static const float CASCADE_INTERVAL = 1.0;
static const int MAX_RAYMARCH_STEPS = 128;

Texture2D EmissiveTexture : register(t0);
Texture2D SceneSDFTexture : register(t1);
Texture2D CascadeTexture : register(t2);

SamplerState SceneColorSampler : register(s0);
SamplerState SceneSDFSampler : register(s1);
SamplerState CascadeSampler : register(s2);

cbuffer CascadeParams : register(b0)
{
    float2 ScreenSize;
    float2 CascadeSize;
    float2 ProbeExtent;
    float2 ProbeSpacing;
    float2 HigherExtent;
    float2 InvProbeExtent;
    float2 InvScreenSize;
    float2 InvCascadeSize;
    float CascadeIndex;
    float CascadeCount;
    float AngularPerAxis;
    float RayOffset;
    float RayRange;
    float SDFScale;
    float ThetaScalar;
    float HigherAngularPerAxis;
};

cbuffer SkyParams : register(b1)
{
    float3 PreCalcSkyColor;
    float PreCalcSunAngle;
    float3 PreCalcSunColor;
    float PreCalcSSunS;
    float PreCalcISSunS;
    bool EnableSkyRadiance;
};

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = float4(input.Position, 1.0);
    output.UV = input.TexCoord;
    return output;
}

float3 ACESToneMapping(float3 color)
{
    float3 x = color;
    return saturate((x * mad(2.51, x, 0.03)) / mad(x, mad(2.43, x, 0.59), 0.14));
}

float3 IntegrateSkyRadiance_Optimized(float2 angle)
{
    float cosA0, cosA1, sinA0, sinA1;
    sincos(angle.x, sinA0, cosA0);
    sincos(angle.y, sinA1, cosA1);

    float3 SI = PreCalcSkyColor * (angle.y - angle.x - 0.5 * (cosA1 - cosA0));

    float atanTerm = (atan(PreCalcSSunS * (PreCalcSunAngle - angle.x)) -
                      atan(PreCalcSSunS * (PreCalcSunAngle - angle.y))) * PreCalcISSunS;

    return mad(PreCalcSunColor, atanTerm, SI) * 0.166667;
}

float3 IntegrateSkyRadiance(float2 angle)
{
    if (angle.y < TAU)
        return IntegrateSkyRadiance_Optimized(angle);
    
    return IntegrateSkyRadiance_Optimized(float2(angle.x, TAU)) +
           IntegrateSkyRadiance_Optimized(float2(0.0, angle.y - TAU));
}

float4 Raymarch(float2 probeOrigin, float rayAngle)
{
    float sinAngle, cosAngle;
    sincos(rayAngle, sinAngle, cosAngle);
    float2 delta = float2(cosAngle, -sinAngle);
    
    float2 rayPos = mad(delta, RayOffset, probeOrigin) * InvScreenSize;
    float2 deltaTexel = delta * InvScreenSize;
    
    float totalDistance = 0.0;
    float prevDistance = 999.0;
    float2 prevPos = rayPos;
    
    [loop]
    for(int i = 0; i < MAX_RAYMARCH_STEPS; i++)
    {
        if (totalDistance >= RayRange)
            break;
        
        float sdfValue = SceneSDFTexture.SampleLevel(SceneSDFSampler, rayPos, 0).r;
        float worldDistance = max(0.0, sdfValue) * SDFScale;  // SDF: -1=inside, 0=edge, +1=outside

        if (worldDistance <= EPSILON)
        {
            if (totalDistance <= EPSILON && CascadeIndex != 0.0)
                return float4(0.0, 0.0, 0.0, 0.0);

            float t = prevDistance / (prevDistance + 0.01);
            float2 hitPos = lerp(prevPos, rayPos, t);

            // Un-premultiply alpha to get true emissive color (avoids flicker from AA edges)
            float4 emissRaw = EmissiveTexture.SampleLevel(SceneColorSampler, hitPos, 0);
            float3 emiss = emissRaw.a > 0.001 ? emissRaw.rgb / emissRaw.a : float3(0, 0, 0);
            return float4(emiss, 0.0);
        }
        
        prevDistance = worldDistance;
        prevPos = rayPos;
        
        float stepDistance = (worldDistance < 4.0) ? 1.0 : worldDistance;
        totalDistance += stepDistance;
        rayPos = mad(deltaTexel, stepDistance, rayPos);
        
        if (any(saturate(rayPos) != rayPos))
            break;
    }
    
    return float4(0.0, 0.0, 0.0, 1.0);
}

float4 MergeWithHigherCascade(float2 probeCoord, float rayIndex, float4 radiance)
{
    if (radiance.a == 0.0)
        return float4(radiance.rgb, 1.0);
    
    if (CascadeIndex >= CascadeCount - 1.0)
    {
        if (EnableSkyRadiance)
        {
            float2 angleRange = float2(rayIndex, rayIndex + 1.0) * ThetaScalar;
            float3 skyRadiance = IntegrateSkyRadiance(angleRange) / ThetaScalar;
            return float4(mad(skyRadiance, radiance.a, radiance.rgb), 1.0);
        }
        return float4(radiance.rgb, 1.0);
    }
    
    float2 blockOffset = float2(
        fmod(rayIndex, HigherAngularPerAxis),
        floor(rayIndex / HigherAngularPerAxis)
    ) * HigherExtent;
    
    float2 higherProbeCoord = clamp(probeCoord * 0.5, 0.0, HigherExtent - 1.0);
    float2 sampleUV = (blockOffset + higherProbeCoord + 0.5) * InvCascadeSize;
    sampleUV = clamp(sampleUV, InvCascadeSize * 0.5, 1.0 - InvCascadeSize * 0.5);
    
    float4 higher = CascadeTexture.SampleLevel(CascadeSampler, sampleUV, 0);
    
    return float4(mad(higher.rgb, radiance.a, radiance.rgb), radiance.a * higher.a);
}

float4 GenerateOutputTexture(PixelShaderInput input) : SV_Target
{
    float2 coord = floor(input.UV * CascadeSize);
    float2 rayBlock = floor(coord * InvProbeExtent);
    float2 probeCoord = coord - rayBlock * ProbeExtent;
    float rayIndex = (rayBlock.x + rayBlock.y * AngularPerAxis) * 4.0;
    float2 probeOrigin = mad(probeCoord, ProbeSpacing, ProbeSpacing * 0.5);
    
    float4 color = 0.0;
    
    [unroll]
    for(int i = 0; i < 4; i++)
    {
        float currentRayIndex = rayIndex + (float)i;
        float rayAngle = mad(currentRayIndex, ThetaScalar, ThetaScalar * 0.5);
        
        float4 radiance = Raymarch(probeOrigin, rayAngle);
        color += MergeWithHigherCascade(probeCoord, currentRayIndex, radiance);
    }

    color.rgb *= 0.25;
    
    if (CascadeIndex == 0.0)
        color.rgb = ACESToneMapping(color.rgb);
    
    return float4(color.rgb, 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 GenerateOutputTexture();
    }
}
