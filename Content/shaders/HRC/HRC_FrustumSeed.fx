/*
	Cascade 0 (c0) has an interval length of 1px, so no raytracing is needed.
	Only compute radiance/transmittance at each ray's origin.
	Get the probe's origin and rotate position for each frustum.

	Each frustum is "right-facing" in memory. Transform the probe
	coordinate so that it samples from the correct world-space position
	for each frustum direction.
*/

// t0/s0 reserved for MonoGame SpriteBatch
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

// Transforms probe coordinate into frustum-local space
// Each frustum is stored "right-facing" and needs rotation to sample correctly
float2 TransformProbeToFrustum(float2 probe, float frustumIndex)
{
    // Rotation transforms for UV coordinates:
    // 90° CW:  (x, y) -> (1-y, x)
    // 180°:    (x, y) -> (1-x, 1-y)
    // 90° CCW: (x, y) -> (y, 1-x)
    float2 offsets[4];
    offsets[0] = probe;                                  // Right-facing (no transform)
    offsets[1] = float2(1.0 - probe.y, probe.x);         // Down-facing (rotate 90° CW)
    offsets[2] = 1.0 - probe;                            // Left-facing (rotate 180°)
    offsets[3] = float2(probe.y, 1.0 - probe.x);         // Up-facing (rotate 90° CCW)

    return offsets[int(frustumIndex)];
}

// Main pixel shader - seeds cascade 0 with initial radiance/transmittance values
// Single render target: RGB = radiance, A = transmittance
float4 MainPS(PixelShaderInput input) : SV_Target0
{
    float2 texel = input.UV * CascadeSize;
    float interval = 1.0;  // c0 interval is always 1px
    float virtualRays = interval + 1.0;
    float plane = floor(texel.x / virtualRays);
    float2 probe = float2((plane * interval) + 0.5, texel.y) / WorldSize;

    // Transform probe to sample from correct frustum orientation
    float2 sampleCoord = TransformProbeToFrustum(probe, FrustumIndex);

    // Sample emissive directly - no color space conversion, keep as-is
    float3 emiss = Emissivity.Sample(EmissiveSampler, sampleCoord).rgb;

    // At origin: radiance = emissive, transmittance = 1.0 (no occlusion)
    return float4(emiss, 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        PixelShader = compile ps_5_0 MainPS();
    }
}
