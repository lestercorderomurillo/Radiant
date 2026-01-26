// UDR3 - Lanczos Upsampling with Temporal Stability

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D LastFrame : register(t3);
Texture2D MotionVectorTexture : register(t4);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float DebugRays;
float FrameCount;
float CurrentWeight;

static const float PI = 3.14159265359;

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

// Lanczos kernel weight
float LanczosWeight(float x, float radius)
{
    if (abs(x) < 0.0001)
        return 1.0;
    if (abs(x) >= radius)
        return 0.0;

    float xpi = x * PI;
    return (sin(xpi) / xpi) * (sin(xpi / radius) / (xpi / radius));
}

// Lanczos 4x4 sampling (radius = 2)
float4 SampleLanczos(Texture2D tex, float2 uv, float2 texSize)
{
    float2 texelSize = 1.0 / texSize;

    // Convert to texel coordinates
    float2 texelPos = uv * texSize - 0.5;
    float2 texelFloor = floor(texelPos);
    float2 frac = texelPos - texelFloor;

    float radius = 2.0;

    // Compute weights for 4 samples in each dimension
    float wx[4], wy[4];
    wx[0] = LanczosWeight(frac.x + 1.0, radius);
    wx[1] = LanczosWeight(frac.x, radius);
    wx[2] = LanczosWeight(frac.x - 1.0, radius);
    wx[3] = LanczosWeight(frac.x - 2.0, radius);

    wy[0] = LanczosWeight(frac.y + 1.0, radius);
    wy[1] = LanczosWeight(frac.y, radius);
    wy[2] = LanczosWeight(frac.y - 1.0, radius);
    wy[3] = LanczosWeight(frac.y - 2.0, radius);

    // Normalize weights
    float sumX = wx[0] + wx[1] + wx[2] + wx[3];
    float sumY = wy[0] + wy[1] + wy[2] + wy[3];
    wx[0] /= sumX; wx[1] /= sumX; wx[2] /= sumX; wx[3] /= sumX;
    wy[0] /= sumY; wy[1] /= sumY; wy[2] /= sumY; wy[3] /= sumY;

    // Sample 4x4 grid and accumulate
    float4 result = float4(0, 0, 0, 0);

    [unroll]
    for (int y = 0; y < 4; y++)
    {
        [unroll]
        for (int x = 0; x < 4; x++)
        {
            float2 offset = float2(x - 1, y - 1);
            float2 sampleUV = (texelFloor + offset + 0.5) * texelSize;
            float weight = wx[x] * wy[y];
            result += tex.Sample(Sampler, sampleUV) * weight;
        }
    }

    return result;
}

// Spatial pass - Lanczos upsampling
float4 Spatial_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    // Lanczos sampling from low-res input
    float4 color = SampleLanczos(InputTexture, uv, InputSize);

    return color;
}

// Temporal pass - motion vector based accumulation
float4 Temporal_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    float3 current = InputTexture.Sample(Sampler, uv).rgb;

    // Read motion vector (encoded as 0.5 = no motion, 0 = -100px, 1 = +100px)
    float2 motionEncoded = MotionVectorTexture.Sample(Sampler, uv).rg;
    float2 velocity = (motionEncoded - 0.5) * 200.0;  // Decode to pixels

    // Calculate velocity magnitude (pixels per frame)
    float speed = length(velocity);

    // Reproject: sample history from where this pixel came from
    float2 historyUV = uv - velocity / OutputSize;
    float3 history = LastFrame.Sample(Sampler, historyUV).rgb;

    // Velocity-based blend weight:
    // speed 0 = static, accumulate normally (low CurrentWeight like 0.08)
    // speed > threshold = fast motion, use current frame more
    static const float SPEED_THRESHOLD = 0.5;   // Start rejecting at 0.5 pixels/frame
    static const float SPEED_SCALE = 2.0;       // How fast to ramp to full rejection

    float motionFactor = saturate((speed - SPEED_THRESHOLD) * SPEED_SCALE);

    // Also add color difference as fallback (catches shadows, lighting changes)
    float3 diff = abs(current - history);
    float colorDiff = max(diff.r, max(diff.g, diff.b));
    float colorMotion = saturate((colorDiff - 0.02) * 10.0);

    motionFactor = max(motionFactor, colorMotion);

    // Static: use normal temporal weight (accumulate)
    // Moving: use mostly current frame (reject history)
    float blendWeight = lerp(CurrentWeight, 1.0, motionFactor);

    float3 result = lerp(history, current, blendWeight);

    return float4(result, 1.0);
}

// Copy pass
float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

technique Spatial
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Spatial_PS();
    }
}

technique Temporal
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Temporal_PS();
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
