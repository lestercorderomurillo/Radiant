// RGB = radiance, A = transmittance (single-channel)
Texture2D VraysCascade : register(t0);
Texture2D PrevCascade : register(t1);

SamplerState SamplerVrays : register(s0);
SamplerState SamplerPrev : register(s1);

float2 VraysSize;
float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;
float ProbeScale;


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

struct PixelShaderOutput
{
    float4 Radiance : SV_Target0;  // RGB = radiance, A = transmittance
};

// Merge radiance with single-channel transmittance in alpha
float4 MergeRadiance(float4 near, float4 far)
{
    return float4(near.rgb + far.rgb * near.a, near.a * far.a);
}

float4 GetVolumeVrays(float2 probe, float index, float interval, float lookupWidth, float4 defVal)
{
    // probe.y is in world space, scale down for half-height texture
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / VraysSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    return lerp(VraysCascade.Sample(SamplerVrays, samplePos), defVal, weight);
}

float4 GetVolumePrev(float2 probe, float index, float interval, float lookupWidth, float4 defVal)
{
    // probe.y is in world space, scale down for half-height texture
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / PrevSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    return lerp(PrevCascade.Sample(SamplerPrev, samplePos), defVal, weight);
}

float4 MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side)
{
    float coneI = index * 2.0 + side;
    float vrayI = index + side;
    float2 limit = float2(intrv, -intrv);
    float align = 2.0 - fmod(plane, 2.0);

    float2 merge = probe + align * (limit + float2(0.0, vrayI * 2.0));

    // Cone angle weighting - use atan(y/x)
    float2 vrayLL = (limit * 2.0) + float2(0.0, coneI * 2.0);
    float2 vrayRR = (limit * 2.0) + float2(0.0, (coneI + 1.0) * 2.0);
    float coneW = atan(vrayRR.y / vrayRR.x) - atan(vrayLL.y / vrayLL.x);

    // defVal: RGB=0 (no radiance), A=1 (full transmittance)
    float4 vray = GetVolumeVrays(probe, vrayI, intrv, vrays, float4(0.0, 0.0, 0.0, 1.0));
    float4 coneFar = GetVolumePrev(merge, coneI, 1.0, 1.0, float4(0.0, 0.0, 0.0, 1.0));

    if (fmod(plane, 2.0) < 0.5)
    {
        // EVEN PLANE: extend ray, then merge with interpolated prev
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float4 vrayExt = GetVolumeVrays(probeFar, vrayI, intrv, vrays, float4(0.0, 0.0, 0.0, 1.0));
        float4 coneNear = GetVolumePrev(probeNear, coneI, 1.0, 1.0, float4(0.0, 0.0, 0.0, 1.0));

        // Extend the ray first
        vray = MergeRadiance(vray, vrayExt);

        // Merge with far cone (with cone weighting)
        float4 weighted = float4(vray.rgb * coneW, vray.a);
        float4 result = MergeRadiance(weighted, coneFar);

        // Interpolate with near cone
        return lerp(result, coneNear, 0.5);
    }
    else
    {
        // ODD PLANE: direct merge with cone weighting
        float3 radiance = (vray.rgb * coneW) + (coneFar.rgb * vray.a);
        float transmit = vray.a * coneFar.a;
        return float4(radiance, transmit);
    }
}

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = exp2(CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / intrv);
    float index = floor(texel.x - (plane * intrv));
    float2 probe = float2(plane * intrv, texel.y * ProbeScale) + float2(0.5, 0.0);

    float4 resultL = MergeCone(probe, plane, intrv, vrays, index, 0.0);
    float4 resultR = MergeCone(probe, plane, intrv, vrays, index, 1.0);

    // Sum radiance, multiply transmittance
    output.Radiance = float4(resultL.rgb + resultR.rgb, resultL.a * resultR.a);
    return output;
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}