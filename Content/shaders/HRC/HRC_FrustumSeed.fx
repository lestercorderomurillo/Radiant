/* HRC_FrustumSeed.fx - MRT Output
   Seeds cascade 0 with emissivity/absorption from scene textures.
   Outputs two targets: Radiance (RGB) and Transmittance (RGB) for stained glass. */

Texture2D Emissivity : register(t0);
Texture2D Absorption : register(t1);

SamplerState EmissiveSampler : register(s0);
SamplerState AbsorptionSampler : register(s1);

float2 WorldSize;
float2 CascadeSize;
float4 FrustumMatrix;  // (m00, m01, m10, m11)
float2 FrustumOffset;
float ProbeScale;      // 1 = full res, 2 = half probes

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

// MRT Output - matching GLSL reference
struct PixelShaderOutput
{
    float4 Radiance     : SV_Target0;  // RGB = radiance
    float4 Transmittance: SV_Target1;  // RGB = transmittance (for stained glass)
};

float2 TransformProbeToFrustum(float2 probe)
{
    return float2(
        probe.x * FrustumMatrix.x + probe.y * FrustumMatrix.y,
        probe.x * FrustumMatrix.z + probe.y * FrustumMatrix.w
    ) + FrustumOffset;
}

// sRGB to Linear conversion (matching GLSL LINEAR macro)
float3 ToLinear(float3 srgb) { return pow(abs(srgb), 2.2); }

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = 1.0;
    float vrays = 2.0;
    float plane = floor(texel.x / vrays);
    float2 probe = float2((plane * intrv) + 0.5, texel.y * ProbeScale) / WorldSize;

    float2 sampleCoord = TransformProbeToFrustum(probe);

    if (sampleCoord.x < 0.0 || sampleCoord.x > 1.0 || sampleCoord.y < 0.0 || sampleCoord.y > 1.0)
    {
        output.Radiance = float4(0.0, 0.0, 0.0, 1.0);      // no radiance
        output.Transmittance = float4(1.0, 1.0, 1.0, 1.0); // full transmittance
        return output;
    }

    // Sample textures (stored in sRGB)
    float3 emissRaw = Emissivity.Sample(EmissiveSampler, sampleCoord).rgb;
    float3 absrpRaw = Absorption.Sample(AbsorptionSampler, sampleCoord).rgb;

    // Convert to linear (matching GLSL: LINEAR(texture2D(...).rgb))
    float3 emiss = ToLinear(emissRaw);
    float3 absrp = ToLinear(absrpRaw);

    // Linear transmittance: absrp 0 = full pass, absrp 1 = full block
    float3 trans = saturate(1.0 - absrp);

    // Radiance from participating media (matching GLSL: (1.0 - trans) * emiss)
    float3 radiance = (1.0 - trans) * emiss;

    output.Radiance = float4(radiance, 1.0);
    output.Transmittance = float4(trans, 1.0);
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
