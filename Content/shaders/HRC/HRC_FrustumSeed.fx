/* HRC_FrustumSeed.fx - MRT Output
   Matches GLSL reference implementation exactly.
   Frustum transforms are identical to reference. */

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

// GLSL reference transforms EXACTLY as written:
// offsets[0] = probe;           // Right
// offsets[1] = 1.0 - probe.yx;  // Down (in GLSL coords)
// offsets[2] = 1.0 - probe;     // Left
// offsets[3] = probe.yx;        // Up (in GLSL coords)
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

    // Match GLSL reference EXACTLY:
    // vec2 texel = in_TexelCoord * cascade_size;
    // float intrv = 1.0;
    // float vrays = 2.0;
    // float plane = floor(texel.x / vrays);
    // vec2 probe = vec2((plane * intrv) + 0.5, texel.y) / world_size;
    float2 texel = input.UV * CascadeSize;
    float intrv = 1.0;
    float vrays = 2.0;
    float plane = floor(texel.x / vrays);
    float2 probe = float2((plane * intrv) + 0.5, texel.y) / WorldSize;

    // Apply frustum transform to probe (matches GLSL: offsets[int(frustum_index)])
    int fIdx = int(FrustumIndex + 0.1);
    float2 sampleCoord = TransformProbeToFrustum(probe, fIdx);

    // Bounds check
    if (sampleCoord.x < 0.0 || sampleCoord.x > 1.0 || sampleCoord.y < 0.0 || sampleCoord.y > 1.0)
    {
        output.Radiance = float4(0.0, 0.0, 0.0, 1.0);
        output.Transmit = float4(1.0, 1.0, 1.0, 1.0);
        return output;
    }

    // Sample scene data
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
