/*
    Sum of Fluence:
    Offset 1px into each frustum, otherwise you get sampling
    overlap between frustums.

    Each frustum is "right-facing" in memory. To fix this
    compute the offset in screen-space then rotate the offset
    coordinate so that it is rotated properly into the frustum.
*/

Texture2D FrustumIndex0 : register(t0);
Texture2D FrustumIndex1 : register(t1);
Texture2D FrustumIndex2 : register(t2);
Texture2D FrustumIndex3 : register(t3);

SamplerState Sampler0 : register(s0);

float2 WorldSize;

#define SRGB(color) pow(color.rgb, float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2))

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

// Main pixel shader - computes sum of fluence from 4 frustum directions
// Each frustum is sampled with offset and rotation to account for frustum orientation
// All frustums are stored "right-facing" in memory and need coordinate transformation
float4 MainPS(float2 texelCoord : TEXCOORD0) : SV_Target0
{
    // Calculate 1 pixel offset in normalized coordinates
    float2 pixelOffset = float2(1.0, 0.0) / WorldSize;

    // Calculate sampling offsets for each frustum direction
    // Each offset moves 1 pixel away from center in a different direction
    float2 offsetRight = texelCoord + pixelOffset.xy;  // +1.0, 0.0  (right)
    float2 offsetDown  = texelCoord - pixelOffset.yx;  // 0.0, -1.0  (down)
    float2 offsetLeft  = texelCoord - pixelOffset.xy;  // -1.0, 0.0  (left)
    float2 offsetUp    = texelCoord + pixelOffset.yx;  // 0.0, +1.0  (up)

    // Sample each frustum with appropriate coordinate transformation
    // Transformations rotate the offset into each frustum's local space
    float3 accumulatedRadiance = float3(0.0, 0.0, 0.0);

    accumulatedRadiance += FrustumIndex0.Sample(Sampler0, offsetRight).rgb;
    accumulatedRadiance += FrustumIndex1.Sample(Sampler0, 1.0 - offsetDown.yx).rgb;
    accumulatedRadiance += FrustumIndex2.Sample(Sampler0, 1.0 - offsetLeft).rgb;
    accumulatedRadiance += FrustumIndex3.Sample(Sampler0, offsetUp.yx).rgb;

    // Average the 4 frustums and convert to sRGB color space
    float3 averageRadiance = accumulatedRadiance / 4.0;

    return float4(SRGB(averageRadiance), 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}
