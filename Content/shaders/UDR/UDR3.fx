// UDR3 - Simple Bilinear Upsampling
// Basic bilinear filtering for testing and comparison

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D LastFrame : register(t3);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
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

// Get luminance for edge detection
float GetLuminance(float3 color)
{
    return dot(color, float3(0.299, 0.587, 0.114));
}

// Sobel edge detection on image luminance
float DetectImageEdge(float2 uv, float2 texelSize)
{
    // Sample 3x3 neighborhood
    float lumTL = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, -texelSize.y)).rgb);
    float lumT  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(0, -texelSize.y)).rgb);
    float lumTR = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, -texelSize.y)).rgb);

    float lumL  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, 0)).rgb);
    float lumR  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, 0)).rgb);

    float lumBL = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, texelSize.y)).rgb);
    float lumB  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(0, texelSize.y)).rgb);
    float lumBR = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, texelSize.y)).rgb);

    // Sobel kernels
    float gx = -lumTL - 2.0 * lumL - lumBL + lumTR + 2.0 * lumR + lumBR;
    float gy = -lumTL - 2.0 * lumT - lumTR + lumBL + 2.0 * lumB + lumBR;

    return sqrt(gx * gx + gy * gy);
}

// SDF-based edge detection (-1 to 1, 0 = edge/center)
float DetectSDFEdge(float2 uv)
{
    // Sample SDF: -1 = inside, 0 = edge, +1 = outside
    float sdfDist = SDFTexture.Sample(Sampler, uv).r;
    return sdfDist;
}

// Simple bilinear upscaling with edge debug
float4 UDR3(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / InputSize;

    // Simple bilinear sampling - the hardware does the work
    float3 result = InputTexture.Sample(Sampler, uv).rgb;

    if (DebugRays > 0.5)
    {
        const float indoorThreshold = 0.005;   // how far green extends INTO the body
        const float outdoorThreshold = 0.00125;  // how far green extends OUT of the body

        // Sample SDF: negative = inside, 0 = edge, positive = outside
        float sdfDist = DetectSDFEdge(uv);

        // Inside the body (negative SDF)
        if (sdfDist < 0 && sdfDist > -indoorThreshold)
        {
            // Gradient: 1.0 at edge (sdfDist=0), fading to 0.0 at -indoorThreshold
            float intensity = 1.0 - abs(sdfDist) / indoorThreshold;
            intensity = smoothstep(0.0, 1.0, intensity);
            return float4(lerp(result, float3(0.0, 1.0, 0.0), intensity * 0.8), 1.0);
        }
        // Outside the body (positive SDF)
        else if (sdfDist > 0 && sdfDist < outdoorThreshold)
        {
            // Gradient: 1.0 at edge (sdfDist=0), fading to 0.0 at outdoorThreshold
            float intensity = 1.0 - sdfDist / outdoorThreshold;
            intensity = smoothstep(0.0, 1.0, intensity);
            return float4(lerp(result, float3(0.0, 1.0, 0.0), intensity * 0.8), 1.0);
        }
    }

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
