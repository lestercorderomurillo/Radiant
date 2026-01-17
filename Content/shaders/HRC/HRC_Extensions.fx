/* HRC_Extensions.fx - MRT Output
   Matches GLSL reference implementation EXACTLY.
   Ray extension combines chained rays from cN-1 to form rays of cN */

Texture2D PrevRadiance : register(t1);
Texture2D PrevTransmit : register(t2);

SamplerState SamplerPrevR : register(s1);
SamplerState SamplerPrevT : register(s2);

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

void GetVolume(float2 probe, float index, float interval, float lookupWidth,
               float2 resolution, Texture2D txtR, SamplerState sampR,
               Texture2D txtT, SamplerState sampT,
               float4 defValR, float4 defValT,
               out float4 rad, out float4 trn)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    // GLSL: float weight = float(floor(samplePos) != vec2(0.0));
    float weight = (samplePos.x < 0.0 || samplePos.x > 1.0 ||
                    samplePos.y < 0.0 || samplePos.y > 1.0) ? 1.0 : 0.0;

    rad = lerp(txtR.Sample(sampR, samplePos), defValR, weight);
    trn = lerp(txtT.Sample(sampT, samplePos), defValT, weight);
}

void ExtendRay(float2 probe, float lo_index, float hi_index,
               float prev_intrv, float prev_vrays,
               out float4 radiance, out float4 transmit)
{
    float2 merge = probe + float2(prev_intrv, -prev_intrv + (lo_index * 2.0));

    float4 radiance_near, transmit_near, radiance_far, transmit_far;

    GetVolume(probe, lo_index, prev_intrv, prev_vrays, PrevSize,
              PrevRadiance, SamplerPrevR, PrevTransmit, SamplerPrevT,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              radiance_near, transmit_near);

    GetVolume(merge, hi_index, prev_intrv, prev_vrays, PrevSize,
              PrevRadiance, SamplerPrevR, PrevTransmit, SamplerPrevT,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              radiance_far, transmit_far);

    MergeRadiance(radiance_near, transmit_near, radiance_far, transmit_far, radiance, transmit);
}

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = pow(2.0, CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / vrays);
    float index = floor(texel.x - (plane * vrays));
    float2 probe = float2(plane * intrv, texel.y) + float2(0.5, 0.0);

    float prev_intrv = pow(2.0, CascadeIndex.x - 1.0);
    float prev_vrays = prev_intrv + 1.0;

    float lower = floor(index * 0.5);
    float upper = ceil(index * 0.5);

    float4 radianceL, radianceU, transmitL, transmitU;
    ExtendRay(probe, lower, upper, prev_intrv, prev_vrays, radianceL, transmitL);
    ExtendRay(probe, upper, lower, prev_intrv, prev_vrays, radianceU, transmitU);

    output.Radiance = lerp(radianceL, radianceU, 0.5);
    output.Transmit = lerp(transmitL, transmitU, 0.5);
    return output;
}

technique GenerateOutputTexture
{
    pass P0 { PixelShader = compile ps_5_0 MainPS(); }
}
