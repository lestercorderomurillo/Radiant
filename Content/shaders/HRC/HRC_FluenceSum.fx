/*
    Sum of Fluence:
    Offset 1px into each frustum, otherwise you get sampling
    overlap between frustums.

    Each frustum is "right-facing" in memory. To fix this
    compute the offset in screen-space then rotate the offset
    coordinate so that it is rotated properly into the frustum.
*/

// t0/s0 reserved for MonoGame SpriteBatch
Texture2D FrustumIndex0 : register(t1);
Texture2D FrustumIndex1 : register(t2);
Texture2D FrustumIndex2 : register(t3);
Texture2D FrustumIndex3 : register(t4);

SamplerState Sampler0 : register(s1);
SamplerState Sampler1 : register(s2);
SamplerState Sampler2 : register(s3);
SamplerState Sampler3 : register(s4);

float2 WorldSize;

#define SRGB(color) pow(color.rgb, float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2))

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
};

// Transforms screen UV to frustum-local sampling coordinate
// Each frustum is stored "right-facing" - need to rotate offset into frustum space
float2 GetFrustumSampleCoord(float2 uv, float2 offset, float frustumIndex)
{
    // Apply offset rotated into frustum space
    float2 rotatedOffset;
    if (frustumIndex == 0.0)
        rotatedOffset = offset;                              // Right: no rotation
    else if (frustumIndex == 1.0)
        rotatedOffset = float2(-offset.y, offset.x);         // Down: 90° CW
    else if (frustumIndex == 2.0)
        rotatedOffset = -offset;                             // Left: 180°
    else
        rotatedOffset = float2(offset.y, -offset.x);         // Up: 90° CCW

    return uv + rotatedOffset;
}

// Main pixel shader - computes sum of fluence from 4 frustum directions
float4 MainPS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 pixelOffset = 1.0 / WorldSize;

    // Sample each frustum with 1px offset to avoid overlap at origin
    float2 coord0 = GetFrustumSampleCoord(uv, pixelOffset, 0.0);
    float2 coord1 = GetFrustumSampleCoord(uv, pixelOffset, 1.0);
    float2 coord2 = GetFrustumSampleCoord(uv, pixelOffset, 2.0);
    float2 coord3 = GetFrustumSampleCoord(uv, pixelOffset, 3.0);

    float3 f0 = FrustumIndex0.Sample(Sampler0, coord0).rgb;
    float3 f1 = FrustumIndex1.Sample(Sampler1, coord1).rgb;
    float3 f2 = FrustumIndex2.Sample(Sampler2, coord2).rgb;
    float3 f3 = FrustumIndex3.Sample(Sampler3, coord3).rgb;

    // Sum fluence from all 4 directions
    float3 totalFluence = f0 + f1 + f2 + f3;

    // Convert linear radiance to sRGB for display
    float3 srgb = SRGB(totalFluence);

    return float4(srgb, 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        PixelShader = compile ps_5_0 MainPS();
    }
}
