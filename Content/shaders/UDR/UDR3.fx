// UDR3 - Simple Bilinear Upsampling
// Basic bilinear filtering for testing and comparison

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D LastFrame : register(t3);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float DetailCorrection;
float DebugRays;
float FrameCount;

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

// Simple bilinear upscaling
float4 UDR3(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    // Simple bilinear sampling - the hardware does the work
    float3 result = InputTexture.Sample(Sampler, uv).rgb;

    return float4(result, 1.0);
}

// Copy pass
float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

technique Bilinear
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR3();
    }
}

technique Copy
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Copy_PS();
    }
}
