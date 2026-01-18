Texture2D VraysRadiance : register(t0);
Texture2D VraysTransmit : register(t1);
Texture2D PrevRadiance : register(t2);
Texture2D PrevTransmit : register(t3);

SamplerState SamplerVraysR : register(s0);
SamplerState SamplerVraysT : register(s1);
SamplerState SamplerPrevR : register(s2);
SamplerState SamplerPrevT : register(s3);

float2 VraysSize;
float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;

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
    float4 Radiance : SV_Target0;
    float4 Transmit : SV_Target1;
};

void MergeRadiance(float4 nearR, float4 nearT, float4 farR, float4 farT,
                   out float4 radiance, out float4 transmit)
{
    radiance = nearR + (farR * nearT);
    transmit = nearT * farT;
}

void GetVolumeVrays(float2 probe, float index, float interval, float lookupWidth,
                    float4 defValR, float4 defValT,
                    out float4 rad, out float4 trn)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / VraysSize;

    float weight = (samplePos.x < 0.0 || samplePos.x >= 1.0 ||
                    samplePos.y < 0.0 || samplePos.y >= 1.0) ? 1.0 : 0.0;

    rad = lerp(VraysRadiance.Sample(SamplerVraysR, samplePos), defValR, weight);
    trn = lerp(VraysTransmit.Sample(SamplerVraysT, samplePos), defValT, weight);
}

void GetVolumePrev(float2 probe, float index, float interval, float lookupWidth,
                   float4 defValR, float4 defValT,
                   out float4 rad, out float4 trn)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / PrevSize;

    float weight = (samplePos.x < 0.0 || samplePos.x >= 1.0 ||
                    samplePos.y < 0.0 || samplePos.y >= 1.0) ? 1.0 : 0.0;

    rad = lerp(PrevRadiance.Sample(SamplerPrevR, samplePos), defValR, weight);
    trn = lerp(PrevTransmit.Sample(SamplerPrevT, samplePos), defValT, weight);
}

void MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side,
               out float4 radiance, out float4 transmit)
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

    float4 vrayR, vrayT, coneFarR, coneFarT;

    GetVolumeVrays(probe, vrayI, intrv, vrays,
                   float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
                   vrayR, vrayT);

    GetVolumePrev(merge, coneI, 1.0, 1.0,
                  float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
                  coneFarR, coneFarT);

    if (fmod(plane, 2.0) < 0.5)
    {
        // EVEN PLANE: extend ray, then merge with interpolated prev
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float4 vrayR_Ext, vrayT_Ext, coneNearR, coneNearT;

        GetVolumeVrays(probeFar, vrayI, intrv, vrays,
                       float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
                       vrayR_Ext, vrayT_Ext);

        GetVolumePrev(probeNear, coneI, 1.0, 1.0,
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
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}