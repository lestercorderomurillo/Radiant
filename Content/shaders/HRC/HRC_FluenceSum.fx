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
    //float2 uv = float2(input.UV.x, 1.0 - input.UV.y);
    float2 uv = float2(input.UV.x, input.UV.y);
    
    float2 pixel = float2(1.0, 0.0) / WorldSize;

    float2 offset0 = uv + pixel.xy;
    float2 offset1 = uv - pixel.yx;
    float2 offset2 = uv - pixel.xy;
    float2 offset3 = uv + pixel.yx;

    float3 r0 = FrustumIndex0.Sample(Sampler, offset0).rgb;
    float3 r1 = FrustumIndex1.Sample(Sampler, 1.0 - offset1.yx).rgb;
    float3 r2 = FrustumIndex2.Sample(Sampler, 1.0 - offset2).rgb;
    float3 r3 = FrustumIndex3.Sample(Sampler, offset3.yx).rgb;

    float3 radiance = r0 + r1 + r2 + r3;

    return float4(ToSRGB(radiance / 4.0), 1.0);
}

technique GenerateOutputTexture
{
    pass P0 { PixelShader = compile ps_5_0 MainPS(); }
}
