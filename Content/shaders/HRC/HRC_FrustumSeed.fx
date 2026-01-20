/* HRC_FrustumSeed.fx - MRT Output
   Seeds cascade 0 with emissivity/absorption from scene textures. */

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

struct PixelShaderOutput
{
    float4 Radiance : COLOR0;
    float4 Transmit : COLOR1;
};

float2 TransformProbeToFrustum(float2 probe)
{
    return float2(
        probe.x * FrustumMatrix.x + probe.y * FrustumMatrix.y,
        probe.x * FrustumMatrix.z + probe.y * FrustumMatrix.w
    ) + FrustumOffset;
}

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
        output.Radiance = float4(0.0, 0.0, 0.0, 1.0);
        output.Transmit = float4(1.0, 1.0, 1.0, 1.0);

        return output;
    }

    float3 emiss = ToLinear(Emissivity.Sample(EmissiveSampler, sampleCoord).rgb);
    float3 absrp = Absorption.Sample(AbsorptionSampler, sampleCoord).rgb * 5.0;

    // Beer's Law
    float3 transmit = exp(-absrp);
    float3 radiance = (1.0 - transmit) * emiss;

    output.Radiance = float4(radiance, 1.0);
    output.Transmit = float4(transmit, 1.0);

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
