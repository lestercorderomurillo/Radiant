// UDR3 - Lanczos Upsampling with Temporal Stability

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D LastFrame : register(t3);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float DebugRays;
float FrameCount;

static const float TEMPORAL_FRAMES = 14.0;          // frames to accumulate (higher = smoother, more ghosting)
static const float TEMPORAL_DIFF_SCALE = 1.77;       // color diff sensitivity (higher = less ghosting, more flicker)
static const float TEMPORAL_OUTER_MARGIN = 0.2;     // SDF distance outside surface for temporal blend
static const float TEMPORAL_INNER_MARGIN = 20.0;    // pixels from edge inside surface for temporal blend

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

float GetLuminance(float3 color)
{
    return dot(color, float3(0.299, 0.587, 0.114));
}

float DetectImageEdge(float2 uv, float2 texelSize)
{
    float lumTL = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, -texelSize.y)).rgb);
    float lumT  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(0, -texelSize.y)).rgb);
    float lumTR = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, -texelSize.y)).rgb);
    float lumL  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, 0)).rgb);
    float lumR  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, 0)).rgb);
    float lumBL = GetLuminance(InputTexture.Sample(Sampler, uv + float2(-texelSize.x, texelSize.y)).rgb);
    float lumB  = GetLuminance(InputTexture.Sample(Sampler, uv + float2(0, texelSize.y)).rgb);
    float lumBR = GetLuminance(InputTexture.Sample(Sampler, uv + float2(texelSize.x, texelSize.y)).rgb);

    float gx = -lumTL - 2.0 * lumL - lumBL + lumTR + 2.0 * lumR + lumBR;
    float gy = -lumTL - 2.0 * lumT - lumTR + lumBL + 2.0 * lumB + lumBR;

    return sqrt(gx * gx + gy * gy);
}

float DetectSDFEdge(float2 uv)
{
    return SDFTexture.Sample(Sampler, uv).r;
}

float GetEdgeBlendFactor(float2 uv)
{
    float2 texelSize = 1.0 / OutputSize;
    float sdfEdge = 0.0;

    float4 emissive = EmissiveTexture.Sample(Sampler, uv);
    float sdfDist = SDFTexture.Sample(Sampler, uv).r;
    bool onSurface = emissive.a > 0.0;

    if (onSurface)
    {
        static const int SAMPLE_COUNT = 8;
        static const float SAMPLE_DISTANCES[8] = { 1.0, 4.0, 8.0, 12.0, 16.0, 20.0, 26.0, 32.0 };
        static const float DIAG = 0.707;

        float lastOnSurface = 0.0;
        float firstOffSurface = TEMPORAL_INNER_MARGIN + 1.0;

        [unroll]
        for (int i = 0; i < SAMPLE_COUNT; i++)
        {
            float dist = SAMPLE_DISTANCES[i];

            float aL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x * dist, 0)).a;
            float aR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x * dist, 0)).a;
            float aU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y * dist)).a;
            float aD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y * dist)).a;

            float diagDist = dist * DIAG;
            float aNW = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x * diagDist, -texelSize.y * diagDist)).a;
            float aNE = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x * diagDist, -texelSize.y * diagDist)).a;
            float aSW = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x * diagDist,  texelSize.y * diagDist)).a;
            float aSE = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x * diagDist,  texelSize.y * diagDist)).a;

            bool hitEdge = (aL < 0.5) || (aR < 0.5) || (aU < 0.5) || (aD < 0.5) ||
                           (aNW < 0.5) || (aNE < 0.5) || (aSW < 0.5) || (aSE < 0.5);

            if (hitEdge)
            {
                firstOffSurface = dist;
                break;
            }
            else
            {
                lastOnSurface = dist;
            }
        }

        if (firstOffSurface <= TEMPORAL_INNER_MARGIN)
        {
            float estimatedDist = (lastOnSurface + firstOffSurface) * 0.5;
            float blendFactor = 1.0 - (estimatedDist / TEMPORAL_INNER_MARGIN);
            sdfEdge = smoothstep(0.0, 1.0, blendFactor);
        }
    }
    else
    {
        float absDist = abs(sdfDist);
        if (absDist <= TEMPORAL_OUTER_MARGIN)
        {
            sdfEdge = 1.0 - (absDist / TEMPORAL_OUTER_MARGIN);
        }
    }

    return sdfEdge;
}

float Lanczos3(float x)
{
    if (x == 0.0) return 1.0;
    if (abs(x) >= 3.0) return 0.0;
    float pi_x = 3.14159265359 * x;
    return (sin(pi_x) * sin(pi_x / 3.0)) / (pi_x * pi_x / 3.0);
}

float4 UDR3_Spatial(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / InputSize;

    float2 srcPos = uv * InputSize - 0.5;
    float2 srcBase = floor(srcPos);
    float2 f = srcPos - srcBase;

    float3 result = 0;
    float3 emissive = 0;
    float totalWeight = 0;

    [unroll] for (int ky = -2; ky <= 3; ky++)
    {
        [unroll] for (int kx = -2; kx <= 3; kx++)
        {
            float2 samplePos = (srcBase + float2(kx, ky) + 0.5) / InputSize;
            float weight = Lanczos3(kx - f.x) * Lanczos3(ky - f.y);
            result += InputTexture.SampleLevel(Sampler, samplePos, 0).rgb * weight;
            emissive += EmissiveTexture.SampleLevel(Sampler, samplePos, 0).rgb * weight;
            totalWeight += weight;
        }
    }

    result /= totalWeight;
    emissive /= totalWeight;

    const float indoorThreshold = 0.0045;
    const float outdoorThreshold = 0.0012;

    float sdfDist = DetectSDFEdge(uv);
    float edgeIntensity = 0.0;

    if (sdfDist < 0 && sdfDist > -indoorThreshold)
    {
        edgeIntensity = 1.0 - abs(sdfDist) / indoorThreshold;
        edgeIntensity = smoothstep(0.0, 1.0, edgeIntensity);
    }
    else if (sdfDist > 0 && sdfDist < outdoorThreshold)
    {
        edgeIntensity = 1.0 - sdfDist / outdoorThreshold;
        edgeIntensity = smoothstep(0.0, 1.0, edgeIntensity);
    }

    if (edgeIntensity > 0.0)
    {
        float4 emissiveSample = EmissiveTexture.Sample(Sampler, uv);
        if (emissiveSample.a > 0.0)
        {
            float3 emissiveColor = emissiveSample.rgb / emissiveSample.a;
            float blendAmount = edgeIntensity * 0.57;

            result = lerp(result, emissiveColor, blendAmount);
        }

        if (DebugRays > 0.5)
        {
            return float4(lerp(result, float3(0.0, 1.0, 0.0), edgeIntensity * 0.8), 1.0);
        }
    }

    return float4(result, 1.0);
}

float4 UDR3_Temporal(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    float3 current = InputTexture.Sample(Sampler, uv).rgb;
    float edgeFactor = GetEdgeBlendFactor(uv);

    if (edgeFactor < 0.001)
    {
        return float4(current, 1.0);
    }

    float3 lastFrame = LastFrame.Sample(Sampler, uv).rgb;

    float3 diff = abs(current - lastFrame);
    float colorDiff = max(diff.r, max(diff.g, diff.b));
    float lastFrameValidity = 1.0 / (1.0 + colorDiff * TEMPORAL_DIFF_SCALE);

    float currentWeight = 1.0 / TEMPORAL_FRAMES;
    float lastFrameWeight = (1.0 - currentWeight) * lastFrameValidity;
    currentWeight = 1.0 - lastFrameWeight;

    float3 temporal = lastFrame * lastFrameWeight + current * currentWeight;
    float3 result = lerp(current, temporal, edgeFactor);

    return float4(result, 1.0);
}

float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

technique Spatial
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR3_Spatial();
    }
}

technique Temporal
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR3_Temporal();
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
