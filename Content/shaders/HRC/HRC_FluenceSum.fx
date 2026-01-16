/*
    Sum of Fluence:
    Offset 1px into each frustum, otherwise you get sampling
    overlap between frustums.
    
    Each frustum is "right-facing," in memory. To fix this
    compute the offset in screen-space then rotate the offset
    coordinate so that it is rotated properly into the frustum.
*/

Texture2D frustumIndex0 : register(t1);
Texture2D frustumIndex1 : register(t2);
Texture2D frustumIndex2 : register(t3);
Texture2D frustumIndex3 : register(t4);

SamplerState sampler1 : register(s1);
SamplerState sampler2 : register(s2);
SamplerState sampler3 : register(s3);
SamplerState sampler4 : register(s4);

cbuffer Constants : register(b0)
{
    float2 worldSize;
};

#define SRGB(color) pow(color.rgb, float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2))

// Main pixel shader - computes sum of fluence from 4 frustum directions
// Each frustum is sampled with offset and rotation to account for frustum orientation
// All frustums are stored "right-facing" in memory and need coordinate transformation
float4 main(float2 texelCoord : TEXCOORD0) : SV_Target0
{
    // Calculate 1 pixel offset in normalized coordinates
    float2 pixelOffset = float2(1.0, 0.0) / worldSize;
    
    // Calculate sampling offsets for each frustum direction
    // Each offset moves 1 pixel away from center in a different direction
    float2 offsetRight = texelCoord + pixelOffset.xy;  // +1.0, 0.0  (right)
    float2 offsetDown  = texelCoord - pixelOffset.yx;  // 0.0, -1.0  (down)
    float2 offsetLeft  = texelCoord - pixelOffset.xy;  // -1.0, 0.0  (left)
    float2 offsetUp    = texelCoord + pixelOffset.yx;  // 0.0, +1.0  (up)
    
    // Sample each frustum with appropriate coordinate transformation
    // Transformations rotate the offset into each frustum's local space
    float3 accumulatedRadiance = float3(0.0, 0.0, 0.0);

    accumulatedRadiance += frustumIndex0.Sample(sampler1, offsetRight).rgb;
    accumulatedRadiance += frustumIndex1.Sample(sampler2, 1.0 - offsetDown.yx).rgb;
    accumulatedRadiance += frustumIndex2.Sample(sampler3, 1.0 - offsetLeft).rgb;
    accumulatedRadiance += frustumIndex3.Sample(sampler4, offsetUp.yx).rgb;
    
    // Average the 4 frustums and convert to sRGB color space
    float3 averageRadiance = accumulatedRadiance / 4.0;
    
    return float4(SRGB(averageRadiance), 1.0);
}