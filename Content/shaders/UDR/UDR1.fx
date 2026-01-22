// UDR - Unified Detail Reconstruction
// Edge-aware spatial upsampling with Lanczos reconstruction

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float Sharpness;
float EdgeCorrection;

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

// Correct edges using SDF overlay at full resolution
float3 CorrectEdges(float3 color, float2 uv)
{
    float4 emissive = EmissiveTexture.Sample(Sampler, uv);
    float sdf = emissive.a; // SDF: positive = on surface, 0 = background

    float surfaceThreshold = 0.5;

    // On surface
    if (sdf > surfaceThreshold)
    {
        // Deep inside surface - just use original upscaled color
        // Near inner edge - blend between upscaled and emissive
        float innerEdgeThreshold = 1.75;
        if (sdf < innerEdgeThreshold)
        {
            // sdf 0.5->1.0 maps to blend 1->0 (more blend near edge, less deep inside)
            float blendFactor = 1.0 - ((sdf - surfaceThreshold) / (innerEdgeThreshold - surfaceThreshold));
            return lerp(color, emissive.rgb, blendFactor);
        }
        return color;
    }

    // Near edge (subpixel zone or outside but close) - blend toward full-res emissive
    // Sample neighborhood to find nearby surfaces
    float2 texelSize = 1.0 / OutputSize;
    float4 emissiveL = EmissiveTexture.Sample(Sampler, uv + float2(-texelSize.x, 0));
    float4 emissiveR = EmissiveTexture.Sample(Sampler, uv + float2( texelSize.x, 0));
    float4 emissiveU = EmissiveTexture.Sample(Sampler, uv + float2(0, -texelSize.y));
    float4 emissiveD = EmissiveTexture.Sample(Sampler, uv + float2(0,  texelSize.y));

    // Find the strongest nearby surface sample
    float maxNeighborSdf = max(max(emissiveL.a, emissiveR.a), max(emissiveU.a, emissiveD.a));

    // Only blend if there's a nearby surface
    if (maxNeighborSdf > surfaceThreshold)
    {
        // Weighted average of full-res emissive colors from neighbors on surface
        float3 neighborEmissive = float3(0, 0, 0);
        float neighborWeight = 0.0;

        if (emissiveL.a > surfaceThreshold) { neighborEmissive += emissiveL.rgb; neighborWeight += 1.0; }
        if (emissiveR.a > surfaceThreshold) { neighborEmissive += emissiveR.rgb; neighborWeight += 1.0; }
        if (emissiveU.a > surfaceThreshold) { neighborEmissive += emissiveU.rgb; neighborWeight += 1.0; }
        if (emissiveD.a > surfaceThreshold) { neighborEmissive += emissiveD.rgb; neighborWeight += 1.0; }

        neighborEmissive /= neighborWeight;

        // Blend factor: subpixel zone (0 < sdf <= 0.5) uses sdf as coverage
        //               outside (sdf <= 0) uses neighbor proximity
        float blendFactor;
        if (sdf > 0.0)
        {
            // Subpixel zone - blend based on coverage (sdf 0->0.5 maps to blend 1->0)
            blendFactor = 1.0 - (sdf / surfaceThreshold);
        }
        else
        {
            // Outside - blend based on neighbor proximity
            blendFactor = saturate((maxNeighborSdf - surfaceThreshold) * 2.0) * 0.5;
        }

        return lerp(color, neighborEmissive, blendFactor);
    }

    return color;
}

// UDR main pixel shader
float4 UDR1_PS(PixelShaderInput input) : SV_Target0
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
        result = CorrectEdges(result, uv);
    }

    return float4(result, 1.0);
}

technique Upscale
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 UDR1_PS();
    }
}
