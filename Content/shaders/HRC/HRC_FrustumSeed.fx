/*
	Cascade 0 (c0) has an interval length of 1px, so no raytracing is needed.
	Only compute radiance/transmittance at each ray's origin.
	Get the probe's origin and rotate position for each frustum.

	Each frustum is "right-facing" in memory. Transform the probe
	coordinate so that it samples from the correct world-space position
	for each frustum direction.
*/

Texture2D Emissivity : register(t0);
Texture2D Absorption : register(t1);

SamplerState Sampler0 : register(s0);

cbuffer Constants : register(b0)
{
    float2 WorldSize;
    float2 CascadeSize;
    float FrustumIndex;
};

#define LINEAR(color) pow(color.rgb, float3(2.2, 2.2, 2.2))

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = float4(input.Position, 1.0);
    output.UV = input.TexCoord;
    return output;
}

// Transforms probe coordinate into frustum-local space
// Each frustum is stored "right-facing" and needs rotation to sample correctly
float2 TransformProbeToFrustum(float2 probe, float frustumIndex)
{
    float2 offsets[4];
    offsets[0] = probe;              // Right-facing (no transform)
    offsets[1] = 1.0 - probe.yx;     // Down-facing (rotate 90° CW + flip)
    offsets[2] = 1.0 - probe;        // Left-facing (rotate 180°)
    offsets[3] = probe.yx;           // Up-facing (rotate 90° CCW)

    return offsets[int(frustumIndex)];
}

// Computes radiance and transmittance from emissivity and absorption
// Uses Beer-Lambert law for light attenuation through participating media
void ComputeRadianceTransmit(float3 emissivity, float3 absorption,
                              out float3 radiance, out float3 transmit)
{
    transmit = exp(-absorption);
    radiance = (1.0 - transmit) * emissivity;
}

// Main pixel shader - seeds cascade 0 with initial radiance/transmittance values
// Samples material properties and computes light contribution at probe origins
void MainPS(float2 texelCoord : TEXCOORD0,
            out float4 outRadiance : SV_Target0,
            out float4 outTransmit : SV_Target1)
{
    float2 texel = texelCoord * CascadeSize;
    float interval = 1.0;  // c0 interval is always 1px
    float virtualRays = interval + 1.0;
    float plane = floor(texel.x / virtualRays);
    float2 probe = float2((plane * interval) + 0.5, texel.y) / WorldSize;

    // Transform probe to sample from correct frustum orientation
    float2 sampleCoord = TransformProbeToFrustum(probe, FrustumIndex);

    // Sample material properties in linear space
    float3 emiss = LINEAR(Emissivity.Sample(Sampler0, sampleCoord).rgb);
    float3 absrp = LINEAR(Absorption.Sample(Sampler0, sampleCoord).rgb);

    // Compute initial radiance and transmittance
    float3 radiance, transmit;
    ComputeRadianceTransmit(emiss, absrp, radiance, transmit);

    outRadiance = float4(radiance, 1.0);
    outTransmit = float4(transmit, 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}
