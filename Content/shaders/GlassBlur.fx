Texture2D InputTexture : register(t0);
SamplerState Sampler0 : register(s0);

float2 TexelSize;
float BlurOffset;
float2 ScreenSize;
float4 WindowRect;
float WindowRadius;
float2 ShadowOffset;
float ShadowSpread;
float ShadowOpacity;

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

float4 PS_Blur(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 off = TexelSize * (BlurOffset + 0.5);
    float4 c = InputTexture.Sample(Sampler0, uv + float2(-off.x, -off.y));

    c += InputTexture.Sample(Sampler0, uv + float2(off.x, -off.y));
    c += InputTexture.Sample(Sampler0, uv + float2(-off.x, off.y));
    c += InputTexture.Sample(Sampler0, uv + float2(off.x, off.y));

    return c * 0.25;
}

float RoundedRectSDF(float2 position, float2 halfSize, float radius)
{
    float2 d = abs(position) - halfSize + radius;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

float4 PS_RoundedBlit(PixelShaderInput input) : SV_Target0
{
    float2 pixel = input.UV * ScreenSize;
    float2 center = WindowRect.xy + WindowRect.zw * 0.5;
    float2 halfSize = WindowRect.zw * 0.5;
    float dist = RoundedRectSDF(pixel - center, halfSize, WindowRadius);
    if (dist >= 0.5) discard;
    float alpha = saturate(0.5 - dist);
    float4 color = InputTexture.Sample(Sampler0, input.UV);
    color.a = 1.0;
    return color * alpha;
}

technique Blur
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader  = compile ps_5_0 PS_Blur();
    }
}

float4 PS_Shadow(PixelShaderInput input) : SV_Target0
{
    float2 pixel = input.UV * ScreenSize;
    float2 center = WindowRect.xy + WindowRect.zw * 0.5 + ShadowOffset;
    float2 halfSize = WindowRect.zw * 0.5;
    float dist = max(RoundedRectSDF(pixel - center, halfSize, WindowRadius), 0.0);
    float alpha = exp(-4.0 * dist * dist / (ShadowSpread * ShadowSpread));
    if (alpha < 0.01) discard;
    return float4(0, 0, 0, ShadowOpacity * alpha);
}

technique RoundedBlit
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader  = compile ps_5_0 PS_RoundedBlit();
    }
}

technique Shadow
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader  = compile ps_5_0 PS_Shadow();
    }
}
