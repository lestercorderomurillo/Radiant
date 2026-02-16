// UDR3 - Lanczos Upsampling with Temporal Stability

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D LastFrame : register(t3);
Texture2D MotionVectorTexture : register(t4);
Texture2D AbsorptionTexture : register(t5);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float DebugRays;
float FrameCount;
float CurrentWeight;

float DebugEdges;
float EdgeCorrection;
float Sharpness;

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

// Edge reconstruction: walk emissive alpha to find edge distance, blend emissive color
static const float EDGE_BLEND = 0.21;
static const float INNER_MARGIN = 8.0;  // max texels from edge (on surface)
static const float OUTER_MARGIN = 1.0;  // max texels from edge (off surface)
static const int   MAX_STEPS = 24;      // walk steps (sample every 2 texels = 8 texel reach)
static const float DIAG = 0.707;

// 8 directions: cardinal + diagonal
static const float2 DIRS[8] = {
    float2( 1, 0), float2(-1, 0), float2(0,  1), float2(0, -1),
    float2( 1, 1) * DIAG, float2(-1, 1) * DIAG,
    float2( 1,-1) * DIAG, float2(-1,-1) * DIAG
};

float WalkToEdge(float2 uv, float2 texelSize, bool onSurface)
{
    // Walk 8 directions, find closest edge (where alpha flips)
    float minDist = INNER_MARGIN + 1.0;

    [unroll]
    for (int d = 0; d < 8; d++)
    {
        [unroll]
        for (int i = 1; i <= MAX_STEPS; i++)
        {
            float dist = i * 2.0;
            float2 sampleUV = uv + DIRS[d] * texelSize * dist;
            float a = EmissiveTexture.Sample(Sampler, sampleUV).a;

            // On surface: look for alpha dropping (leaving geometry)
            // Off surface: look for alpha appearing (entering geometry)
            bool hitEdge = onSurface ? (a < 0.5) : (a >= 0.5);

            if (hitEdge)
            {
                // Diagonal steps are further apart
                float actualDist = (d >= 4) ? dist * DIAG : dist;
                minDist = min(minDist, actualDist);
                break;
            }
        }
    }
    return minDist;
}

float4 EdgeReconstruct_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / OutputSize;

    float3 upsampled = InputTexture.Sample(Sampler, uv).rgb;

    if (EdgeCorrection <= 0.0)
        return float4(upsampled, 1.0);

    float4 emissive = EmissiveTexture.Sample(Sampler, uv);

    bool onSurface = emissive.a > 0.5;
    float margin = onSurface ? INNER_MARGIN : OUTER_MARGIN;

    // Walk emissive alpha to find distance to nearest edge
    float edgeDist = WalkToEdge(uv, texelSize, onSurface);

    // Far from edge: no correction
    if (edgeDist > margin)
    {
        if (DebugEdges > 0.5)
            return float4(0, 0, 0, 1);
        return float4(upsampled, 1.0);
    }

    // Blend factor: 1 at edge, 0 at margin
    float blendFactor = 1.0 - (edgeDist / margin);
    float absorption = AbsorptionTexture.Sample(Sampler, uv).a;
    blendFactor = smoothstep(0.0, 1.0, blendFactor) * EDGE_BLEND * absorption;

    // On surface: blend emissive in near edges
    // Off surface: pull in nearest surface emissive color
    float3 edgeColor;

    if (onSurface)
    {
        edgeColor = emissive.rgb;
        // Transparent surfaces: reduce blend by absorption alpha to avoid darkening inside
        blendFactor *= absorption;
    }
    else
    {
        // Sample neighbors to find nearest surface color + its absorption
        float3 surfaceColor = float3(0, 0, 0);
        float surfaceWeight = 0.0;
        float surfaceAbsorption = 0.0;

        [unroll]
        for (int d = 0; d < 8; d++)
        {
            [unroll]
            for (int i = 1; i <= MAX_STEPS; i++)
            {
                float2 sampleUV = uv + DIRS[d] * texelSize * i * 2.0;
                float4 em = EmissiveTexture.Sample(Sampler, sampleUV);
                if (em.a >= 0.5)
                {
                    float w = 1.0 / (i * i);
                    surfaceColor += em.rgb * w;
                    surfaceAbsorption += AbsorptionTexture.Sample(Sampler, sampleUV).a * w;
                    surfaceWeight += w;
                    break;
                }
            }
        }
        edgeColor = surfaceWeight > 0.0 ? surfaceColor / surfaceWeight : upsampled;

        // Transparent nearby surfaces: reduce outer edge correction
        float nearAbsorption = surfaceWeight > 0.0 ? surfaceAbsorption / surfaceWeight : 0.0;
        blendFactor *= nearAbsorption;
    }

    float3 result = lerp(upsampled, edgeColor, blendFactor);

    // Debug: edge proximity as white
    if (DebugEdges > 0.5)
        return float4(blendFactor, blendFactor, blendFactor, 1.0);

    return float4(max(result, 0.0), 1.0);
}

// Temporal pass - color difference based accumulation
float4 Temporal_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    float3 current = InputTexture.Sample(Sampler, uv).rgb;
    float3 history = LastFrame.Sample(Sampler, uv).rgb;

    // Color difference for history validity
    float3 diff = abs(current - history);
    float colorDiff = max(diff.r, max(diff.g, diff.b));

    static const float DIFF_SCALE = 2.0;
    float historyValidity = 1.0 / (1.0 + colorDiff * DIFF_SCALE);

    // Running average: new_avg = old_avg * (1 - 1/N) + current * (1/N)
    float historyWeight = (1.0 - CurrentWeight) * historyValidity;
    float currentWeight = 1.0 - historyWeight;

    float3 result = history * historyWeight + current * currentWeight;

    return float4(result, 1.0);
}

// Copy pass
float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

// RCAS - Robust Contrast-Adaptive Sharpening (simple unsharp mask style)
float4 RCAS_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / OutputSize;

    // 5-tap cross
    float3 c = InputTexture.Sample(Sampler, uv).rgb;
    float3 n = InputTexture.Sample(Sampler, uv + float2(0, -texelSize.y)).rgb;
    float3 s = InputTexture.Sample(Sampler, uv + float2(0,  texelSize.y)).rgb;
    float3 e = InputTexture.Sample(Sampler, uv + float2( texelSize.x, 0)).rgb;
    float3 w = InputTexture.Sample(Sampler, uv + float2(-texelSize.x, 0)).rgb;

    // Local average
    float3 avg = (n + s + e + w) * 0.25;

    // Sharpen: center + (center - average) * strength
    float3 result = c + (c - avg) * Sharpness;

    return float4(max(result, 0.0), 1.0);
}

technique UDR3_Stage1 // Spatial - Lanczos upsampling
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Spatial_PS();
    }
}

technique UDR3_Stage2 // Edge reconstruction
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 EdgeReconstruct_PS();
    }
}

technique UDR3_Stage3 // Temporal accumulation
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Temporal_PS();
    }
}

technique UDR3_Stage4 // Copy to history
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 Copy_PS();
    }
}

technique UDR3_Stage5 // RCAS sharpening
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 RCAS_PS();
    }
}
