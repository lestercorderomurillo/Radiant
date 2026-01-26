// UDR2 - Unified Detail Reconstruction with Temporal Stability
// Edge-aware spatial upsampling with Lanczos reconstruction + temporal accumulation

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
Texture2D SDFTexture : register(t2);
Texture2D HistoryTexture : register(t3);
Texture2D AbsorptionTexture : register(t4);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float Sharpness;
float EdgeCorrection;
float DebugRays;

// Temporal parameters
float CurrentWeight;          // Weight for current frame (1/N where N = frames accumulated)
float FrameCount;             // Number of frames accumulated so far

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

// Un-premultiply alpha to get true color from premultiplied texture
float3 UnpremultiplyRGB(float4 premultiplied)
{
    return premultiplied.a > 0.001 ? premultiplied.rgb / premultiplied.a : float3(0, 0, 0);
}

static const float PI = 3.14159265359;

// Get Lanczos weight
float GetLanczosWeight(float x, float radius)
{
    if (abs(x) < 0.0001) return 1.0;
    if (abs(x) >= radius) return 0.0;

    float xpi = x * PI;
    return (sin(xpi) / xpi) * (sin(xpi / radius) / (xpi / radius));
}

// 12-tap neighborhood sample data
struct NeighborhoodSamples
{
    float3 b, c;           // Row -1
    float3 e, f, g, h;     // Row 0
    float3 i, j, k, l;     // Row 1
    float3 n, o;           // Row 2
};

// Luminance values for all neighborhood samples
struct NeighborhoodLuminance
{
    float b, c;
    float e, f, g, h;
    float i, j, k, l;
    float n, o;
};

// Edge detection results
struct EdgeInfo
{
    float horizontal;
    float vertical;
    float strength;
    float hWeight;
    float vWeight;
};

// Sample 12-tap neighborhood pattern
//    b c
//  e f g h
//  i j k l
//    n o
NeighborhoodSamples SampleNeighborhood(float2 baseUV, float2 texelSize)
{
    NeighborhoodSamples neighborhood;

    neighborhood.b = InputTexture.Sample(Sampler, baseUV + float2( 0, -1) * texelSize).rgb;
    neighborhood.c = InputTexture.Sample(Sampler, baseUV + float2( 1, -1) * texelSize).rgb;

    neighborhood.e = InputTexture.Sample(Sampler, baseUV + float2(-1,  0) * texelSize).rgb;
    neighborhood.f = InputTexture.Sample(Sampler, baseUV + float2( 0,  0) * texelSize).rgb;
    neighborhood.g = InputTexture.Sample(Sampler, baseUV + float2( 1,  0) * texelSize).rgb;
    neighborhood.h = InputTexture.Sample(Sampler, baseUV + float2( 2,  0) * texelSize).rgb;

    neighborhood.i = InputTexture.Sample(Sampler, baseUV + float2(-1,  1) * texelSize).rgb;
    neighborhood.j = InputTexture.Sample(Sampler, baseUV + float2( 0,  1) * texelSize).rgb;
    neighborhood.k = InputTexture.Sample(Sampler, baseUV + float2( 1,  1) * texelSize).rgb;
    neighborhood.l = InputTexture.Sample(Sampler, baseUV + float2( 2,  1) * texelSize).rgb;

    neighborhood.n = InputTexture.Sample(Sampler, baseUV + float2( 0,  2) * texelSize).rgb;
    neighborhood.o = InputTexture.Sample(Sampler, baseUV + float2( 1,  2) * texelSize).rgb;

    return neighborhood;
}

// Get luminance for all neighborhood samples
NeighborhoodLuminance GetNeighborhoodLuminance(NeighborhoodSamples neighborhood)
{
    NeighborhoodLuminance lum;

    lum.b = GetLuminance(neighborhood.b); lum.c = GetLuminance(neighborhood.c);
    lum.e = GetLuminance(neighborhood.e); lum.f = GetLuminance(neighborhood.f);
    lum.g = GetLuminance(neighborhood.g); lum.h = GetLuminance(neighborhood.h);
    lum.i = GetLuminance(neighborhood.i); lum.j = GetLuminance(neighborhood.j);
    lum.k = GetLuminance(neighborhood.k); lum.l = GetLuminance(neighborhood.l);
    lum.n = GetLuminance(neighborhood.n); lum.o = GetLuminance(neighborhood.o);

    return lum;
}

// Find edges using cross gradients of luminance
EdgeInfo FindEdges(NeighborhoodLuminance lum)
{
    EdgeInfo edge;

    // Horizontal edge detection
    float edgeH1 = abs(lum.e - lum.f) + abs(lum.f - lum.g) + abs(lum.g - lum.h);
    float edgeH2 = abs(lum.i - lum.j) + abs(lum.j - lum.k) + abs(lum.k - lum.l);

    edge.horizontal = edgeH1 + edgeH2;

    // Vertical edge detection
    float edgeV1 = abs(lum.b - lum.f) + abs(lum.f - lum.j) + abs(lum.j - lum.n);
    float edgeV2 = abs(lum.c - lum.g) + abs(lum.g - lum.k) + abs(lum.k - lum.o);

    edge.vertical = edgeV1 + edgeV2;

    // Determine dominant edge direction
    float edgeTotal = edge.horizontal + edge.vertical + 0.0001;

    edge.hWeight = edge.vertical / edgeTotal;   // More vertical edges = use horizontal interpolation
    edge.vWeight = edge.horizontal / edgeTotal; // More horizontal edges = use vertical interpolation

    // Edge strength determines blend between Lanczos and edge-aware
    edge.strength = saturate((edge.horizontal + edge.vertical) * 4.0);

    return edge;
}

// Reconstruct using Lanczos 4x4 (radius = 2)
float3 ReconstructLanczos(NeighborhoodSamples neighborhood, float2 frac)
{
    float radius = 2.0;

    // Get Lanczos weights for the 4x4 grid
    float wx0 = GetLanczosWeight(frac.x + 1.0, radius);  // e, i column
    float wx1 = GetLanczosWeight(frac.x, radius);        // f, j column
    float wx2 = GetLanczosWeight(frac.x - 1.0, radius);  // g, k column
    float wx3 = GetLanczosWeight(frac.x - 2.0, radius);  // h, l column

    float wy0 = GetLanczosWeight(frac.y + 1.0, radius);  // b, c row
    float wy1 = GetLanczosWeight(frac.y, radius);        // e, f, g, h row
    float wy2 = GetLanczosWeight(frac.y - 1.0, radius);  // i, j, k, l row
    float wy3 = GetLanczosWeight(frac.y - 2.0, radius);  // n, o row

    // Normalize weights
    float sumX = wx0 + wx1 + wx2 + wx3;
    float sumY = wy0 + wy1 + wy2 + wy3;
    wx0 /= sumX; wx1 /= sumX; wx2 /= sumX; wx3 /= sumX;
    wy0 /= sumY; wy1 /= sumY; wy2 /= sumY; wy3 /= sumY;

    // Row -1 (only b, c available, use linear for missing samples)
    float w01 = wx1 + wx0 * 0.5;
    float w02 = wx2 + wx3 * 0.5;
    float3 row0 = (neighborhood.b * w01 + neighborhood.c * w02) / (w01 + w02);

    // Row 0 (e, f, g, h available)
    float3 row1 = neighborhood.e * wx0 + neighborhood.f * wx1 + neighborhood.g * wx2 + neighborhood.h * wx3;

    // Row 1 (i, j, k, l available)
    float3 row2 = neighborhood.i * wx0 + neighborhood.j * wx1 + neighborhood.k * wx2 + neighborhood.l * wx3;

    // Row 2 (only n, o available, use linear for missing samples)
    float3 row3 = (neighborhood.n * w01 + neighborhood.o * w02) / (w01 + w02);

    return row0 * wy0 + row1 * wy1 + row2 * wy2 + row3 * wy3;
}

// Edge-aware interpolation refinement
float3 RefineEdges(NeighborhoodSamples neighborhood, float3 lanczos, float2 frac, EdgeInfo edge)
{
    // Compute edge-aware samples (stretch along edges)
    float3 hBlend = lerp(lerp(neighborhood.e, neighborhood.f, frac.x), lerp(neighborhood.g, neighborhood.h, frac.x), 0.5);
    float3 vBlend = lerp(lerp(neighborhood.b, neighborhood.c, frac.x), lerp(neighborhood.n, neighborhood.o, frac.x), 0.5);

    // Blend based on edge direction
    float3 edgeAware = lerp(
        lerp(lanczos, hBlend, edge.hWeight * edge.strength * 0.5),
        lerp(lanczos, vBlend, edge.vWeight * edge.strength * 0.5),
        0.5
    );

    // Final blend: use more Lanczos in smooth areas, more edge-aware on edges
    return lerp(lanczos, edgeAware, edge.strength * 0.7);
}

// Sharpen using RCAS-style adaptive method
float3 Sharpen(float3 color, NeighborhoodSamples neighborhood)
{
    // Get local contrast
    float3 minColor = min(min(min(neighborhood.f, neighborhood.g), neighborhood.j), neighborhood.k);
    float3 maxColor = max(max(max(neighborhood.f, neighborhood.g), neighborhood.j), neighborhood.k);
    float3 contrast = maxColor - minColor;

    // Adaptive sharpening - less sharpening in high contrast areas
    float contrastLum = GetLuminance(contrast);
    float adaptiveSharp = Sharpness * saturate(1.0 - contrastLum * 2.0);

    // Sharpen using the bilinear neighborhood
    float3 neighbors = (neighborhood.f + neighborhood.g + neighborhood.j + neighborhood.k) * 0.25;
    float3 sharpened = color + (color - neighbors) * adaptiveSharp;

    // Clamp to prevent ringing artifacts
    return clamp(sharpened, minColor, maxColor);
}

// Edge correction using SDF
float4 CorrectEdgesWithDebug(float3 color, float2 uv)
{
    // Adjustable constants
    static const float OUTER_MARGIN = 0.05;  // SDF is now -1 to 1
    static const float INNER_MARGIN = 18.0;
    static const float EDGE_BLEND = 0.10;

    float4 emissive = EmissiveTexture.Sample(Sampler, uv);
    float sdfDist = SDFTexture.Sample(Sampler, uv).r;  // 0 = on surface, >0 = outside
    bool onSurface = emissive.a > 0.0;

    float2 texelSize = 1.0 / OutputSize;

    if (onSurface)
    {
        // ON SURFACE - sample every 2 texels until edge or 24 texels
        // Sample 8 directions: cardinal + diagonal
        float edgeDist = INNER_MARGIN + 1.0;
        static const float DIAG = 0.707;  // 1/sqrt(2) for diagonal distance

        [unroll]
        for (int i = 1; i <= 12; i++)
        {
            float dist = i * 2.0;

            // Cardinal directions
            float aL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x * dist, 0)).a;
            float aR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x * dist, 0)).a;
            float aU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y * dist)).a;
            float aD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y * dist)).a;

            // Diagonal directions
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

        // Deep inside - no SDF correction needed
        if (edgeDist > INNER_MARGIN)
        {
            return float4(color, 0.0);
        }

        // Smooth gradient: 0 at edge, 1 at INNER_MARGIN
        float blendFactor = edgeDist / INNER_MARGIN;
        blendFactor = smoothstep(0.0, 1.0, blendFactor);

        // blendFactor: 0 at edge, 1 deep inside
        // At edge: use emissive (full-res, fills holes)
        // Deep inside: use color (original upscaled, already good)

        // Apply EDGE_BLEND to control max emissive contribution at edge
        float emissiveAmount = (1.0 - blendFactor) * EDGE_BLEND;

        // Get surface opacity from absorption texture
        float surfaceOpacity = AbsorptionTexture.Sample(Sampler, uv).a;

        // Skip edge correction for transparent surfaces - HRC already handles transmitted light correctly
        static const float OPACITY_THRESHOLD = 0.5;
        if (surfaceOpacity < OPACITY_THRESHOLD)
        {
            return float4(color, 0.0);  // No correction for transparent surfaces
        }

        // Blend: emissive fills holes near edge, original color preserved inside
        // debug = emissiveAmount remapped to show correction intensity (positive = on surface)
        float3 emissiveColor = UnpremultiplyRGB(emissive);

        return float4(lerp(color, emissiveColor, emissiveAmount), emissiveAmount + 0.5);
    }
    else
    {
        // OUTSIDE SURFACE - blend toward neighbor emissive colors
        // SDF is now -1 to 1, use abs() for distance
        float absDist = abs(sdfDist);

        // Far from any surface - no correction needed
        if (absDist > OUTER_MARGIN)
        {
            return float4(color, 0.0);  // debug = 0 (no correction)
        }

        // Sample neighbor emissive colors and their absorption
        float4 emissiveL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x, 0));
        float4 emissiveR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x, 0));
        float4 emissiveU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y));
        float4 emissiveD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y));

        // Weighted average from neighbors on surface (un-premultiply to get true colors)
        // Also accumulate opacity for proper blending
        float3 surfaceColor = float3(0, 0, 0);
        float surfaceWeight = 0.0;
        float avgOpacity = 0.0;

        // Only consider opaque neighbors for edge correction
        static const float OPACITY_THRESHOLD = 0.5;

        if (emissiveL.a > 0.0) {
            float opacityL = AbsorptionTexture.Sample(Sampler, uv + float2(-texelSize.x, 0)).a;
            if (opacityL >= OPACITY_THRESHOLD) {
                surfaceColor += UnpremultiplyRGB(emissiveL);
                surfaceWeight += 1.0;
            }
        }
        if (emissiveR.a > 0.0) {
            float opacityR = AbsorptionTexture.Sample(Sampler, uv + float2(texelSize.x, 0)).a;
            if (opacityR >= OPACITY_THRESHOLD) {
                surfaceColor += UnpremultiplyRGB(emissiveR);
                surfaceWeight += 1.0;
            }
        }
        if (emissiveU.a > 0.0) {
            float opacityU = AbsorptionTexture.Sample(Sampler, uv + float2(0, -texelSize.y)).a;
            if (opacityU >= OPACITY_THRESHOLD) {
                surfaceColor += UnpremultiplyRGB(emissiveU);
                surfaceWeight += 1.0;
            }
        }
        if (emissiveD.a > 0.0) {
            float opacityD = AbsorptionTexture.Sample(Sampler, uv + float2(0, texelSize.y)).a;
            if (opacityD >= OPACITY_THRESHOLD) {
                surfaceColor += UnpremultiplyRGB(emissiveD);
                surfaceWeight += 1.0;
            }
        }

        // No nearby opaque surface - no correction
        if (surfaceWeight < 0.001)
        {
            return float4(color, 0.0);  // debug = 0 (no correction)
        }

        surfaceColor /= surfaceWeight;

        // Blend factor: 1 at edge (absDist=0), 0 at OUTER_MARGIN
        float blendFactor = 1.0 - (absDist / OUTER_MARGIN);
        blendFactor *= EDGE_BLEND;

        // debug = negative values for outside surface correction (0.5 - blendFactor maps to 0-0.5 range)
        return float4(lerp(color, surfaceColor, blendFactor), 0.5 - blendFactor);
    }
}

float3 CorrectEdges(float3 color, float2 uv)
{
    return CorrectEdgesWithDebug(color, uv).rgb;
}

float3 ClampHistory(float3 history, float3 minColor, float3 maxColor)
{
    return clamp(history, minColor, maxColor);
}

// Spatial pass
float4 UDR2_Spatial_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / InputSize;

    // Calculate position in input texture space
    float2 inputPos = uv * InputSize - 0.5;
    float2 inputPosFloor = floor(inputPos);
    float2 frac = inputPos - inputPosFloor;
    float2 baseUV = (inputPosFloor + 0.5) * texelSize;

    // Sample neighborhood and get luminance
    NeighborhoodSamples samples = SampleNeighborhood(baseUV, texelSize);
    NeighborhoodLuminance luminance = GetNeighborhoodLuminance(samples);

    // Find edges
    EdgeInfo edge = FindEdges(luminance);

    // Reconstruct with Lanczos
    float3 lanczos = ReconstructLanczos(samples, frac);

    // Refine edges
    float3 result = RefineEdges(samples, lanczos, frac, edge);

    // Sharpen (optional)
    if (Sharpness > 0.0)
    {
        result = Sharpen(result, samples);
    }

    // Correct edges (optional)
    if (EdgeCorrection > 0.0)
    {
        if (DebugRays > 0.0)
        {
            // Debug: GREEN = SDF edges
            float4 corrected = CorrectEdgesWithDebug(result, uv);
            float sdfDebug = corrected.w;
            result = corrected.rgb;

            if (sdfDebug > 0.5)
            {
                float intensity = (sdfDebug - 0.5) * 2.0;
                result = lerp(result, float3(0.0, 1.0, 0.0), intensity * 0.7);
            }
        }
        else
        {
            result = CorrectEdges(result, uv);
        }
    }

    return float4(result, 1.0);
}

// Temporal pass

// SDF-based edge detection for temporal blending
float GetEdgeBlendFactor(float2 uv, float outerMarginMultiplier)
{
    static const float BASE_OUTER_MARGIN = 0.05;  // SDF is now -1 to 1, need larger margin
    static const float INNER_MARGIN = 20.0;

    float outerMargin = BASE_OUTER_MARGIN * outerMarginMultiplier;
    float2 texelSize = 1.0 / OutputSize;
    float sdfEdge = 0.0;

    float4 emissive = EmissiveTexture.Sample(Sampler, uv);
    float sdfDist = SDFTexture.Sample(Sampler, uv).r;
    bool onSurface = emissive.a > 0.0;

    if (onSurface)
    {
        // ON SURFACE - find distance to edge (8 directions: cardinal + diagonal)
        static const int SAMPLE_COUNT = 8;
        static const float SAMPLE_DISTANCES[8] = { 1.0, 4.0, 8.0, 12.0, 16.0, 20.0, 26.0, 32.0 };
        static const float DIAG = 0.707;

        float lastOnSurface = 0.0;
        float firstOffSurface = INNER_MARGIN + 1.0;

        [unroll]
        for (int i = 0; i < SAMPLE_COUNT; i++)
        {
            float dist = SAMPLE_DISTANCES[i];

            // Cardinal
            float aL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x * dist, 0)).a;
            float aR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x * dist, 0)).a;
            float aU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y * dist)).a;
            float aD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y * dist)).a;

            // Diagonal
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

        // Near SDF edge
        if (firstOffSurface <= INNER_MARGIN)
        {
            float estimatedDist = (lastOnSurface + firstOffSurface) * 0.5;
            float blendFactor = 1.0 - (estimatedDist / INNER_MARGIN);
            sdfEdge = smoothstep(0.0, 1.0, blendFactor);
        }
    }
    else
    {
        // OUTSIDE SURFACE - SDF is now -1 to 1, use abs() for distance
        float absDist = abs(sdfDist);
        if (absDist <= outerMargin)
        {
            sdfEdge = 1.0 - (absDist / outerMargin);
        }
    }

    return sdfEdge;
}

float4 UDR2_Temporal_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;

    // Sample current frame (from spatial pass)
    float3 current = InputTexture.Sample(Sampler, uv).rgb;

    // Check if we're near an edge (extends further outside than edge correction)
    float edgeFactor = GetEdgeBlendFactor(uv, 2.0);  // 2x outer margin for temporal

    // Not near edge - return current frame directly (no temporal blending)
    if (edgeFactor < 0.001)
    {
        return float4(current, 1.0);
    }

    // Near edge - apply temporal blending
    float3 history = HistoryTexture.Sample(Sampler, uv).rgb;

    // Blend rate based on color difference: higher diff = lower history weight
    float3 diff = abs(current - history);
    float colorDiff = max(diff.r, max(diff.g, diff.b));

    static const float DIFF_SCALE = 2.0;  // How fast history weight drops with difference

    // historyValidity: 1.0 when identical, approaches 0 as difference increases
    float historyValidity = 1.0 / (1.0 + colorDiff * DIFF_SCALE);

    // Running average: new_avg = old_avg * (1 - 1/N) + current * (1/N)
    // CurrentWeight = 1/N where N = number of frames to accumulate
    float historyWeight = (1.0 - CurrentWeight) * historyValidity;
    float currentWeight = 1.0 - historyWeight;

    // Blend temporal result based on edge proximity
    float3 temporal = history * historyWeight + current * currentWeight;

    // Interpolate between current (no temporal) and temporal based on edge factor
    float3 result = lerp(current, temporal, edgeFactor);

    return float4(result, 1.0);
}

// Edge smoothing
float4 EdgeSmooth_PS(PixelShaderInput input) : SV_Target0
{
    static const float SMOOTH_STRENGTH = 0.90;
    static const float CONTRAST_THRESHOLD = 0.015;
    static const float RELATIVE_THRESHOLD = 0.03;

    float2 uv = input.UV;
    float2 texelSize = 1.0 / OutputSize;

    // Sample center and neighbors
    float3 colorM = InputTexture.Sample(Sampler, uv).rgb;
    float3 colorN = InputTexture.Sample(Sampler, uv + float2(0, -texelSize.y)).rgb;
    float3 colorS = InputTexture.Sample(Sampler, uv + float2(0,  texelSize.y)).rgb;
    float3 colorW = InputTexture.Sample(Sampler, uv + float2(-texelSize.x, 0)).rgb;
    float3 colorE = InputTexture.Sample(Sampler, uv + float2( texelSize.x, 0)).rgb;

    float lumM = GetLuminance(colorM);
    float lumN = GetLuminance(colorN);
    float lumS = GetLuminance(colorS);
    float lumW = GetLuminance(colorW);
    float lumE = GetLuminance(colorE);

    float lumMin = min(lumM, min(min(lumN, lumS), min(lumW, lumE)));
    float lumMax = max(lumM, max(max(lumN, lumS), max(lumW, lumE)));
    float lumRange = lumMax - lumMin;

    float threshold = max(CONTRAST_THRESHOLD, lumMax * RELATIVE_THRESHOLD);

    // Not an edge - return original
    if (lumRange < threshold)
    {
        return float4(colorM, 1.0);
    }

    // Edge detected - apply Gaussian blur (9x9, sigma ~2.0)
    float3 blur = float3(0, 0, 0);
    float totalWeight = 0.0;

    [unroll]
    for (int y = -5; y <= 5; y++)
    {
        [unroll]
        for (int x = -5; x <= 5; x++)
        {
            float2 offset = float2(x, y) * texelSize;
            float dist = sqrt(float(x * x + y * y));
            float weight = exp(-(dist * dist) / 8.0);  // sigma = 2.0

            blur += InputTexture.Sample(Sampler, uv + offset).rgb * weight;
            totalWeight += weight;
        }
    }
    blur /= totalWeight;

    float3 result = lerp(colorM, blur, SMOOTH_STRENGTH);
    return float4(result, 1.0);
}

// Copy
float4 Copy_PS(PixelShaderInput input) : SV_Target0
{
    return InputTexture.Sample(Sampler, input.UV);
}

technique Spatial
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR2_Spatial_PS();
    }
}

technique Temporal
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR2_Temporal_PS();
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
