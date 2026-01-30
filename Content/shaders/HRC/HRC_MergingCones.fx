/* HRC_MergingCones.fx
   Merges cascade rays with RGB transmittance for stained glass support. */

Texture2D VraysRadiance : register(t0);
Texture2D VraysTransmittance : register(t1);
Texture2D PrevRadiance : register(t2);
Texture2D PrevTransmittance : register(t3);

SamplerState Sampler0 : register(s0);

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
    float4 Radiance     : SV_Target0;
    float4 Transmittance: SV_Target1;
};

void MergeRadiance(float3 nearRad, float3 nearTrans, float3 farRad, float3 farTrans,
                   out float3 outRad, out float3 outTrans)
{
    outRad = nearRad + farRad * nearTrans;
    outTrans = nearTrans * farTrans;
}

void GetVolumeVrays(float2 probe, float index, float interval, float lookupWidth,
                    out float3 rad, out float3 trans)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / VraysSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    rad = lerp(VraysRadiance.Sample(Sampler0, samplePos).rgb, float3(0, 0, 0), weight);
    trans = lerp(VraysTransmittance.Sample(Sampler0, samplePos).rgb, float3(1, 1, 1), weight);
}

void GetVolumePrev(float2 probe, float index, float interval, float lookupWidth,
                   out float3 rad, out float3 trans)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / PrevSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    rad = lerp(PrevRadiance.Sample(Sampler0, samplePos).rgb, float3(0, 0, 0), weight);
    trans = lerp(PrevTransmittance.Sample(Sampler0, samplePos).rgb, float3(1, 1, 1), weight);
}

void MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side,
               out float3 outRad, out float3 outTrans)
{
    float coneI = index * 2.0 + side;
    float vrayI = index + side;
    float2 limit = float2(intrv, -intrv);
    float align = 2.0 - fmod(plane, 2.0);

    float2 merge = probe + align * (limit + float2(0.0, vrayI * 2.0));

    float2 vrayLL = (limit * 2.0) + float2(0.0, coneI * 2.0);
    float2 vrayRR = (limit * 2.0) + float2(0.0, (coneI + 1.0) * 2.0);
    float coneW = atan(vrayRR.y / vrayRR.x) - atan(vrayLL.y / vrayLL.x);

    float3 vrayRad, vrayTrans, coneFarRad, coneFarTrans;
    GetVolumeVrays(probe, vrayI, intrv, vrays, vrayRad, vrayTrans);
    GetVolumePrev(merge, coneI, 1.0, 1.0, coneFarRad, coneFarTrans);

    if (fmod(plane, 2.0) < 0.5)
    {
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float3 vrayExtRad, vrayExtTrans, coneNearRad, coneNearTrans;
        GetVolumeVrays(probeFar, vrayI, intrv, vrays, vrayExtRad, vrayExtTrans);
        GetVolumePrev(probeNear, coneI, 1.0, 1.0, coneNearRad, coneNearTrans);

        float3 extRad, extTrans;
        MergeRadiance(vrayRad, vrayTrans, vrayExtRad, vrayExtTrans, extRad, extTrans);

        float3 weightedRad = extRad * coneW;
        float3 resultRad, resultTrans;
        MergeRadiance(weightedRad, extTrans, coneFarRad, coneFarTrans, resultRad, resultTrans);

        outRad = lerp(resultRad, coneNearRad, 0.5);
        outTrans = lerp(resultTrans, coneNearTrans, 0.5);
    }
    else
    {
        outRad = (vrayRad * coneW) + (coneFarRad * vrayTrans);
        outTrans = vrayTrans * coneFarTrans;
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

    float3 radL, transL, radR, transR;
    MergeCone(probe, plane, intrv, vrays, index, 0.0, radL, transL);
    MergeCone(probe, plane, intrv, vrays, index, 1.0, radR, transR);

    output.Radiance = float4(radL + radR, 1.0);
    output.Transmittance = float4(transL + transR, 1.0);
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
