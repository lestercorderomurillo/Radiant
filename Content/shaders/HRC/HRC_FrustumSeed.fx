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
    float4 Radiance : COLOR0;  // RGB = radiance, A = transmittance
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
        output.Radiance = float4(0.0, 0.0, 0.0, 1.0);  // full transmittance
        return output;
    }

    float4 emissRaw = Emissivity.Sample(EmissiveSampler, sampleCoord);
    // Un-premultiply alpha to get true emissive color (avoids flicker from AA edges)
    float3 emiss = emissRaw.a > 0.001 ? ToLinear(emissRaw.rgb / emissRaw.a) : float3(0, 0, 0);
    float3 absrp = Absorption.Sample(AbsorptionSampler, sampleCoord).rgb * 5.0;

    // Beer's Law with luminance-weighted single-channel transmittance
    float absrpLum = dot(absrp, float3(0.299, 0.587, 0.114));
    float transmit = exp(-absrpLum);
    float3 radiance = (1.0 - transmit) * emiss;

    output.Radiance = float4(radiance, transmit);
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
