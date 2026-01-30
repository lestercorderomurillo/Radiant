/* HRC_Extensions.fx
   Ray extension combines chained rays from cascade N-1 to form rays of cascade N.
   Uses separate textures for radiance and transmittance (RGB each). */

Texture2D PrevRadiance : register(t0);
Texture2D PrevTransmittance : register(t1);

SamplerState Sampler0 : register(s0);

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

void GetVolume(float2 probe, float index, float interval, float lookupWidth,
               float2 resolution, out float3 rad, out float3 trans)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    float3 sampledRad = PrevRadiance.Sample(Sampler0, samplePos).rgb;
    float3 sampledTrans = PrevTransmittance.Sample(Sampler0, samplePos).rgb;

    rad = lerp(sampledRad, float3(0, 0, 0), weight);
    trans = lerp(sampledTrans, float3(1, 1, 1), weight);
}

void ExtendRay(float2 probe, float loIndex, float hiIndex,
               float prevIntrv, float prevVrays,
               out float3 rad, out float3 trans)
{
    float2 merge = probe + float2(prevIntrv, -prevIntrv + (loIndex * 2.0));

    float3 nearRad, nearTrans, farRad, farTrans;
    GetVolume(probe, loIndex, prevIntrv, prevVrays, PrevSize, nearRad, nearTrans);
    GetVolume(merge, hiIndex, prevIntrv, prevVrays, PrevSize, farRad, farTrans);

    MergeRadiance(nearRad, nearTrans, farRad, farTrans, rad, trans);
}

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = exp2(CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / vrays);
    float index = floor(texel.x - (plane * vrays));
    float2 probe = float2(plane * intrv, texel.y * ProbeScale) + float2(0.5, 0.0);

    float prevIntrv = exp2(CascadeIndex.x - 1.0);
    float prevVrays = prevIntrv + 1.0;

    float lower = floor(index * 0.5);
    float upper = ceil(index * 0.5);

    float3 radL, transL, radU, transU;
    ExtendRay(probe, lower, upper, prevIntrv, prevVrays, radL, transL);
    ExtendRay(probe, upper, lower, prevIntrv, prevVrays, radU, transU);

    output.Radiance = float4(lerp(radL, radU, 0.5), 1.0);
    output.Transmittance = float4(lerp(transL, transU, 0.5), 1.0);
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
