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
float CurrentWeight;  // Dynamic weight from C#: 1/min(frameIndex+1, maxFrames)

// Edge correction thresholds (emissive overlay)
static const float CORRECTION_OUTER_MARGIN = 0.0030;  // SDF distance outside surface
static const float CORRECTION_INNER_MARGIN = 64.00;   // pixels from edge inside surface

// Blur thresholds
static const float BLUR_OUTER_MARGIN = 0.035;            // SDF distance outside surface for blur
static const float BLUR_INNER_MARGIN = 16.000;           // pixels from edge inside surface for blur

// Temporal parameters
static const float TEMPORAL_DIFF_SCALE = 1.50;       // color diff sensitivity (higher = less ghosting, more flicker)

// Edge blend strengths
static const float EDGE_BLEND_INSIDE = 0.29;         // blend strength when inside emissive surface
static const float EDGE_BLEND_OUTSIDE = 0.77;        // blend strength when outside emissive surface

// Edge smooth parameters
static const float SMOOTH_STRENGTH = 0.70;
static const float BLUR_SIGMA = 3.00;                 // blur strength: 1.0 = subtle, 1.5 = medium, 2.0+ = strong
static const int BLUR_RADIUS = 5;                    // kernel radius: 2 = 5x5, 3 = 7x7, 4 = 9x9

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

float GetEdgeBlendFactorEx(float2 uv, float outerMargin, float innerMargin)
{
    float2 texelSize = 1.0 / OutputSize;
    float sdfEdge = 0.0;

    float4 emissive = EmissiveTexture.Sample(Sampler, uv);
    float sdfDist = SDFTexture.Sample(Sampler, uv).r;
    bool onSurface = emissive.a > 0.0;

    if (onSurface)
    {
        // Non-linear sampling (denser near, sparser far) - max 64 pixels
        static const int SAMPLE_COUNT = 16;
        static const float SAMPLE_DISTANCES[16] = { 1.0, 2.0, 4.0, 6.0, 8.0, 12.0, 16.0, 20.0, 26.0, 32.0, 40.0, 48.0, 56.0, 64.0, 72.0, 80.0 };
        static const float DIAG = 0.707;
        float edgeDist = innerMargin + 1.0;

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
                edgeDist = dist;
                break;
            }
        }

        if (edgeDist <= innerMargin)
        {
            float blendFactor = 1.0 - (edgeDist / innerMargin);
            sdfEdge = smoothstep(0.0, 1.0, blendFactor);
        }
    }
    else
    {
        // OUTSIDE SURFACE - use SDF distance
        float absDist = abs(sdfDist);
        if (absDist <= outerMargin)
        {
            sdfEdge = 1.0 - (absDist / outerMargin);
        }
    }

    return sdfEdge;
}

// Convenience wrappers
float GetCorrectionBlendFactor(float2 uv)
{
    return GetEdgeBlendFactorEx(uv, CORRECTION_OUTER_MARGIN, CORRECTION_INNER_MARGIN);
}

float GetBlurBlendFactor(float2 uv)
{
    return GetEdgeBlendFactorEx(uv, BLUR_OUTER_MARGIN, BLUR_INNER_MARGIN);
}

static const float PI = 3.14159265359;

float Lanczos2(float x)
{
    if (abs(x) < 0.0001) return 1.0;
    if (abs(x) >= 2.0) return 0.0;
    float xpi = x * PI;
    return (sin(xpi) / xpi) * (sin(xpi / 2.0) / (xpi / 2.0));
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

    [unroll] for (int ky = -1; ky <= 2; ky++)
    {
        [unroll] for (int kx = -1; kx <= 2; kx++)
        {
            float2 samplePos = (srcBase + float2(kx, ky) + 0.5) / InputSize;
            float weight = Lanczos2(kx - f.x) * Lanczos2(ky - f.y);
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
            float blendAmount = edgeIntensity * 0.37;

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
    float2 texelSize = 1.0 / OutputSize;

    float3 current = InputTexture.Sample(Sampler, uv).rgb;

    // Get both factors
    float blurFactor = GetBlurBlendFactor(uv);
    float correctionFactor = GetCorrectionBlendFactor(uv);

    // Check if we're inside emissive surface
    float4 emissiveSample = EmissiveTexture.Sample(Sampler, uv);
    bool insideEmissive = emissiveSample.a > 0.0;

    // Not near any edge - return current directly (no temporal)
    if (blurFactor < 0.001 && correctionFactor < 0.001)
    {
        return float4(current, 1.0);
    }

    float3 result = current;

    // Step 1: Temporal accumulation - ONLY on edge areas
    float edgeFactor = max(blurFactor, correctionFactor);

    {
        float3 history = LastFrame.Sample(Sampler, uv).rgb;

        // Blend rate based on color difference: higher diff = lower history weight
        float3 diff = abs(current - history);
        float colorDiff = max(diff.r, max(diff.g, diff.b));
        float historyValidity = 1.0 / (1.0 + colorDiff * TEMPORAL_DIFF_SCALE);

        // Running average: CurrentWeight = 1/N from C# (dynamic)
        float historyWeight = (1.0 - CurrentWeight) * historyValidity;
        float finalCurrentWeight = 1.0 - historyWeight;

        float3 temporal = history * historyWeight + current * finalCurrentWeight;
        result = lerp(current, temporal, edgeFactor);
    }

    // Step 2: Apply emissive/SDF correction on top
    if (correctionFactor > 0.001)
    {
        if (insideEmissive)
        {
            float3 emissiveColor = emissiveSample.rgb / emissiveSample.a;
            result = lerp(result, emissiveColor, correctionFactor * EDGE_BLEND_INSIDE);
        }
        else
        {
            float4 emissiveL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x, 0));
            float4 emissiveR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x, 0));
            float4 emissiveU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y));
            float4 emissiveD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y));

            float3 surfaceColor = float3(0, 0, 0);
            float surfaceWeight = 0.0;

            if (emissiveL.a > 0.0) { surfaceColor += emissiveL.rgb / emissiveL.a; surfaceWeight += 1.0; }
            if (emissiveR.a > 0.0) { surfaceColor += emissiveR.rgb / emissiveR.a; surfaceWeight += 1.0; }
            if (emissiveU.a > 0.0) { surfaceColor += emissiveU.rgb / emissiveU.a; surfaceWeight += 1.0; }
            if (emissiveD.a > 0.0) { surfaceColor += emissiveD.rgb / emissiveD.a; surfaceWeight += 1.0; }

            if (surfaceWeight > 0.0)
            {
                surfaceColor /= surfaceWeight;
                result = lerp(result, surfaceColor, correctionFactor * EDGE_BLEND_OUTSIDE);
            }
        }
    }

    // Step 3: Gaussian blur at the end - ONLY outside emissive surfaces
    // Sample from LastFrame (temporal history) to blur stabilized content, not raw input
    // Multiply by luminance so bright pixels glow, dark pixels stay sharp
    float luminance = GetLuminance(current);
    float finalBlurFactor = blurFactor * luminance;

    if (finalBlurFactor > 0.001 && !insideEmissive)
    {
        float3 blur = float3(0, 0, 0);
        float totalWeight = 0.0;
        float sigma2 = 2.0 * BLUR_SIGMA * BLUR_SIGMA;

        [unroll]
        for (int y = -BLUR_RADIUS; y <= BLUR_RADIUS; y++)
        {
            [unroll]
            for (int x = -BLUR_RADIUS; x <= BLUR_RADIUS; x++)
            {
                float2 offset = float2(x, y) * texelSize;
                float dist2 = float(x * x + y * y);
                float weight = exp(-dist2 / sigma2);

                blur += LastFrame.Sample(Sampler, uv + offset).rgb * weight;
                totalWeight += weight;
            }
        }
        blur /= totalWeight;

        result = lerp(result, blur, finalBlurFactor * SMOOTH_STRENGTH);
    }

    // Debug visualization
    if (DebugRays > 0.5)
    {
        float3 debugColor = result;

        // Red = blur, Green = correction (layered)
        if (blurFactor > 0.001)
        {
            debugColor = lerp(debugColor, float3(1.0, 0.0, 0.0), blurFactor * 0.8);
        }
        if (correctionFactor > 0.001)
        {
            debugColor = lerp(debugColor, float3(0.0, 1.0, 0.0), correctionFactor * 0.8);
        }

        return float4(debugColor, 1.0);
    }

    return float4(result, 1.0);
}

float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

// Edge smoothing - blur first, then apply emissive correction on top
float4 EdgeSmooth_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / OutputSize;

    float3 current = InputTexture.Sample(Sampler, uv).rgb;

    // Separate factors for blur and correction
    float blurFactor = GetBlurBlendFactor(uv);
    float correctionFactor = GetCorrectionBlendFactor(uv);

    // Not near any edge - return original
    if (blurFactor < 0.001 && correctionFactor < 0.001)
    {
        return float4(current, 1.0);
    }

    float3 result = current;

    // Step 1: Gaussian blur to smooth blocky upscaled pixels
    if (blurFactor > 0.001)
    {
        float3 blur = float3(0, 0, 0);
        float totalWeight = 0.0;
        float sigma2 = 2.0 * BLUR_SIGMA * BLUR_SIGMA;

        [unroll]
        for (int y = -BLUR_RADIUS; y <= BLUR_RADIUS; y++)
        {
            [unroll]
            for (int x = -BLUR_RADIUS; x <= BLUR_RADIUS; x++)
            {
                float2 offset = float2(x, y) * texelSize;
                float dist2 = float(x * x + y * y);
                float weight = exp(-dist2 / sigma2);

                blur += InputTexture.Sample(Sampler, uv + offset).rgb * weight;
                totalWeight += weight;
            }
        }
        blur /= totalWeight;

        result = lerp(result, blur, blurFactor * SMOOTH_STRENGTH);
    }

    // Step 2: Apply emissive/SDF correction on top of blurred result
    if (correctionFactor > 0.001)
    {
        float4 emissiveSample = EmissiveTexture.Sample(Sampler, uv);

        if (emissiveSample.a > 0.0)
        {
            // On surface - blend with emissive color
            float3 emissiveColor = emissiveSample.rgb / emissiveSample.a;
            result = lerp(result, emissiveColor, correctionFactor * EDGE_BLEND_INSIDE);
        }
        else
        {
            // Outside surface - sample neighbor emissive colors
            float4 emissiveL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x, 0));
            float4 emissiveR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x, 0));
            float4 emissiveU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y));
            float4 emissiveD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y));

            float3 surfaceColor = float3(0, 0, 0);
            float surfaceWeight = 0.0;

            if (emissiveL.a > 0.0) { surfaceColor += emissiveL.rgb / emissiveL.a; surfaceWeight += 1.0; }
            if (emissiveR.a > 0.0) { surfaceColor += emissiveR.rgb / emissiveR.a; surfaceWeight += 1.0; }
            if (emissiveU.a > 0.0) { surfaceColor += emissiveU.rgb / emissiveU.a; surfaceWeight += 1.0; }
            if (emissiveD.a > 0.0) { surfaceColor += emissiveD.rgb / emissiveD.a; surfaceWeight += 1.0; }

            if (surfaceWeight > 0.0)
            {
                surfaceColor /= surfaceWeight;
                result = lerp(result, surfaceColor, correctionFactor * EDGE_BLEND_OUTSIDE);
            }
        }
    }

    if (DebugRays > 0.5)
    {
        // Red = blur only, Yellow = both, Green = correction only
        float3 debugColor = result;

        // Show red for blur-only areas (blur but no correction)
        if (blurFactor > 0.001 && correctionFactor < 0.001)
        {
            debugColor = lerp(debugColor, float3(1.0, 0.0, 0.0), blurFactor * 0.8);
        }
        // Show yellow for overlap areas (both blur and correction)
        else if (blurFactor > 0.001 && correctionFactor > 0.001)
        {
            float maxFactor = max(blurFactor, correctionFactor);
            debugColor = lerp(debugColor, float3(1.0, 1.0, 0.0), maxFactor * 0.8);
        }
        // Show green for correction-only areas
        else if (correctionFactor > 0.001)
        {
            debugColor = lerp(debugColor, float3(0.0, 1.0, 0.0), correctionFactor * 0.8);
        }

        return float4(debugColor, 1.0);
    }

    return float4(result, 1.0);
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

technique EdgeSmooth
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 EdgeSmooth_PS();
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
