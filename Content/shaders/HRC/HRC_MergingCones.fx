/* HRC_MergingCones.fx - MRT Output
   Matches GLSL reference implementation EXACTLY.

   Key insight from GLSL:
   - getVolume for prev uses interval=1.0, lookupWidth=1.0 (direct sample)
   - Cone weighting using atan for angular coverage
   - Even planes: extend ray THEN merge with interpolated prev
   - Odd planes: direct merge with cone weighting
*/

Texture2D VraysRadiance : register(t1);
Texture2D VraysTransmit : register(t2);
Texture2D PrevRadiance : register(t3);
Texture2D PrevTransmit : register(t4);

SamplerState SamplerVraysR : register(s1);
SamplerState SamplerVraysT : register(s2);
SamplerState SamplerPrevR : register(s3);
SamplerState SamplerPrevT : register(s4);

float2 VraysSize;
float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
};

struct PixelShaderOutput
{
    float4 Radiance : COLOR0;
    float4 Transmit : COLOR1;
};

void MergeRadiance(float4 nearR, float4 nearT, float4 farR, float4 farT,
                   out float4 radiance, out float4 transmit)
{
    radiance = nearR + (farR * nearT);
    transmit = nearT * farT;
}

// Generic volume lookup matching GLSL reference exactly
void GetVolume(float2 probe, float index, float interval, float lookupWidth,
               float2 resolution, Texture2D txtR, SamplerState sampR,
               Texture2D txtT, SamplerState sampT,
               float4 defValR, float4 defValT,
               out float4 rad, out float4 trn)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    // GLSL: float weight = float(floor(samplePos) != vec2(0.0));
    // This is bounds check - if outside [0,1] use default
    float weight = (samplePos.x < 0.0 || samplePos.x > 1.0 ||
                    samplePos.y < 0.0 || samplePos.y > 1.0) ? 1.0 : 0.0;

    rad = lerp(txtR.Sample(sampR, samplePos), defValR, weight);
    trn = lerp(txtT.Sample(sampT, samplePos), defValT, weight);
}

void MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side,
               out float4 radiance, out float4 transmit)
{
    float coneI = index * 2.0 + side;
    float vrayI = index + side;
    float2 limit = float2(intrv, -intrv);
    float align = 2.0 - fmod(plane, 2.0);

    float2 merge = probe + align * (limit + float2(0.0, vrayI * 2.0));

    // Cone angle weighting (matches GLSL reference)
    float2 vrayLL = (limit * 2.0) + float2(0.0, coneI * 2.0);
    float2 vrayRR = (limit * 2.0) + float2(0.0, (coneI + 1.0) * 2.0);
    float coneW = atan2(vrayRR.y, vrayRR.x) - atan2(vrayLL.y, vrayLL.x);

    float4 vrayR, vrayT, coneFarR, coneFarT;

    // Sample from vrays with full lookup
    GetVolume(probe, vrayI, intrv, vrays, VraysSize,
              VraysRadiance, SamplerVraysR, VraysTransmit, SamplerVraysT,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              vrayR, vrayT);

    // Sample from prev with interval=1.0, lookupWidth=1.0 (direct sample)
    GetVolume(merge, coneI, 1.0, 1.0, PrevSize,
              PrevRadiance, SamplerPrevR, PrevTransmit, SamplerPrevT,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              coneFarR, coneFarT);

    if (fmod(plane, 2.0) < 0.5)
    {
        // EVEN PLANE: extend ray, then merge with interpolated prev
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float4 vrayR_Ext, vrayT_Ext, coneNearR, coneNearT;

        GetVolume(probeFar, vrayI, intrv, vrays, VraysSize,
                  VraysRadiance, SamplerVraysR, VraysTransmit, SamplerVraysT,
                  float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
                  vrayR_Ext, vrayT_Ext);

        GetVolume(probeNear, coneI, 1.0, 1.0, PrevSize,
                  PrevRadiance, SamplerPrevR, PrevTransmit, SamplerPrevT,
                  float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
                  coneNearR, coneNearT);

        // Extend the ray first
        MergeRadiance(vrayR, vrayT, vrayR_Ext, vrayT_Ext, vrayR, vrayT);

        // Merge with far cone (with cone weighting)
        MergeRadiance(vrayR * coneW, vrayT, coneFarR, coneFarT, radiance, transmit);

        // Interpolate with near cone
        radiance = lerp(radiance, coneNearR, 0.5);
        transmit = lerp(transmit, coneNearT, 0.5);
    }
    else
    {
        // ODD PLANE: direct merge with cone weighting
        radiance = (vrayR * coneW) + (coneFarR * vrayT);
        transmit = vrayT * coneFarT;
    }
}

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = pow(2.0, CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / intrv);
    float index = floor(texel.x - (plane * intrv));
    float2 probe = float2(plane * intrv, texel.y) + float2(0.5, 0.0);

    float4 radL, radR, trnL, trnR;
    MergeCone(probe, plane, intrv, vrays, index, 0.0, radL, trnL);
    MergeCone(probe, plane, intrv, vrays, index, 1.0, radR, trnR);

    output.Radiance = radL + radR;
    output.Transmit = trnL + trnR;
    return output;
}

technique GenerateOutputTexture
{
    pass P0 { PixelShader = compile ps_5_0 MainPS(); }
}
