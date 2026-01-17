/* HRC_FluenceSum.fx
   Matches GLSL reference EXACTLY.

   GLSL Reference Implementation:
   vec2 pixel = vec2(1.0, 0.0) / world_size;
   offsets[0] = in_TexelCoord + pixel.xy;
   offsets[1] = in_TexelCoord - pixel.yx;
   offsets[2] = in_TexelCoord - pixel.xy;
   offsets[3] = in_TexelCoord + pixel.yx;

   radiance += texture2D(frustum_index0, offsets[0]).rgb;
   radiance += texture2D(frustum_index1, 1.0 - offsets[1].yx).rgb;
   radiance += texture2D(frustum_index2, 1.0 - offsets[2]).rgb;
   radiance += texture2D(frustum_index3, offsets[3].yx).rgb;
*/

Texture2D FrustumIndex0 : register(t1);
Texture2D FrustumIndex1 : register(t2);
Texture2D FrustumIndex2 : register(t3);
Texture2D FrustumIndex3 : register(t4);

SamplerState Sampler : register(s1);

float2 WorldSize;

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
};

float3 ToSRGB(float3 linearColor) { return pow(abs(linearColor), 1.0 / 2.2); }

float4 MainPS(PixelShaderInput input) : SV_Target0
{
    // Match GLSL reference EXACTLY
    float2 pixel = float2(1.0, 0.0) / WorldSize;

    // Compute offsets for each frustum
    float2 offset0 = input.UV + pixel.xy;  // Right
    float2 offset1 = input.UV - pixel.yx;  // Up (in GLSL coords, but HLSL Y is flipped)
    float2 offset2 = input.UV - pixel.xy;  // Left
    float2 offset3 = input.UV + pixel.yx;  // Down (in GLSL coords, but HLSL Y is flipped)

    // Sample each frustum with its transform
    // GLSL: texture2D(frustum_index0, offsets[0]).rgb
    float3 r0 = FrustumIndex0.Sample(Sampler, offset0).rgb;

    // GLSL: texture2D(frustum_index1, 1.0 - offsets[1].yx).rgb
    float3 r1 = FrustumIndex1.Sample(Sampler, 1.0 - offset1.yx).rgb;

    // GLSL: texture2D(frustum_index2, 1.0 - offsets[2]).rgb
    float3 r2 = FrustumIndex2.Sample(Sampler, 1.0 - offset2).rgb;

    // GLSL: texture2D(frustum_index3, offsets[3].yx).rgb
    float3 r3 = FrustumIndex3.Sample(Sampler, offset3.yx).rgb;

    // Sum and average
    float3 radiance = r0 + r1 + r2 + r3;

    return float4(ToSRGB(radiance / 4.0), 1.0);
}

technique GenerateOutputTexture
{
    pass P0 { PixelShader = compile ps_5_0 MainPS(); }
}
