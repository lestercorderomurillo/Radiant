/* HRC_FrustumSeed.fx - MRT Output
   Seeds cascade 0 with emissivity/absorption from scene textures. */

Texture2D SpriteBatchTexture : register(t0);
Texture2D Emissivity : register(t1);
Texture2D Absorption : register(t2);

SamplerState SpriteBatchSampler : register(s0);
SamplerState EmissiveSampler : register(s1);
SamplerState AbsorptionSampler : register(s2);

float2 WorldSize;
float2 CascadeSize;
float FrustumIndex;

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


float2 TransformProbeToFrustum(float2 probe, int index)
{
    if (index == 0) return probe;
    if (index == 1) return 1.0 - probe.yx;
    if (index == 2) return 1.0 - probe;
    if (index == 3) return probe.yx;
    return probe;
}

float3 ToLinear(float3 srgb) { return pow(abs(srgb), 2.2); }

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = 1.0;
    float vrays = 2.0;
    float plane = floor(texel.x / vrays);
    float2 probe = float2((plane * intrv) + 0.5, texel.y) / WorldSize;

    int fIdx = int(FrustumIndex + 0.1);
    float2 sampleCoord = TransformProbeToFrustum(probe, fIdx);
    //sampleCoord.y = 1.0 - sampleCoord.y;

    if (sampleCoord.x < 0.0 || sampleCoord.x > 1.0 || sampleCoord.y < 0.0 || sampleCoord.y > 1.0)
    {
        output.Radiance = float4(0.0, 0.0, 0.0, 1.0);
        output.Transmit = float4(1.0, 1.0, 1.0, 1.0);
        return output;
    }

    float3 emiss = ToLinear(Emissivity.Sample(EmissiveSampler, sampleCoord).rgb);
    float3 absrp = ToLinear(Absorption.Sample(AbsorptionSampler, sampleCoord).rgb);

    // Beer's Law
    float3 transmit = exp(-absrp);
    float3 radiance = (1.0 - transmit) * emiss;

    output.Radiance = float4(radiance, 1.0);
    output.Transmit = float4(transmit, 1.0);
    return output;
}

technique GenerateOutputTexture
{
    pass P0 { PixelShader = compile ps_5_0 MainPS(); }
}
