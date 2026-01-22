// UDR - Unified Detail Reconstruction
// Edge-aware spatial upsampling with Lanczos reconstruction

Texture2D InputTexture : register(t0);
Texture2D EmissiveTexture : register(t1);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float Sharpness;
float EdgeOverlay;

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

// Compute luminance for edge detection
float Luminance(float3 color)
{
    return dot(color, float3(0.299, 0.587, 0.114));
}

// Lanczos-like weight function
float LanczosWeight(float x, float radius)
{
    if (abs(x) < 0.0001) return 1.0;
    if (abs(x) >= radius) return 0.0;

    float pi = 3.14159265359;
    float xpi = x * pi;
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
    NeighborhoodSamples s;

    s.b = InputTexture.Sample(Sampler, baseUV + float2( 0, -1) * texelSize).rgb;
    s.c = InputTexture.Sample(Sampler, baseUV + float2( 1, -1) * texelSize).rgb;

    s.e = InputTexture.Sample(Sampler, baseUV + float2(-1,  0) * texelSize).rgb;
    s.f = InputTexture.Sample(Sampler, baseUV + float2( 0,  0) * texelSize).rgb;
    s.g = InputTexture.Sample(Sampler, baseUV + float2( 1,  0) * texelSize).rgb;
    s.h = InputTexture.Sample(Sampler, baseUV + float2( 2,  0) * texelSize).rgb;

    s.i = InputTexture.Sample(Sampler, baseUV + float2(-1,  1) * texelSize).rgb;
    s.j = InputTexture.Sample(Sampler, baseUV + float2( 0,  1) * texelSize).rgb;
    s.k = InputTexture.Sample(Sampler, baseUV + float2( 1,  1) * texelSize).rgb;
    s.l = InputTexture.Sample(Sampler, baseUV + float2( 2,  1) * texelSize).rgb;

    s.n = InputTexture.Sample(Sampler, baseUV + float2( 0,  2) * texelSize).rgb;
    s.o = InputTexture.Sample(Sampler, baseUV + float2( 1,  2) * texelSize).rgb;

    return s;
}

// Compute luminance for all neighborhood samples
NeighborhoodLuminance ComputeNeighborhoodLuminance(NeighborhoodSamples s)
{
    NeighborhoodLuminance lum;

    lum.b = Luminance(s.b); lum.c = Luminance(s.c);
    lum.e = Luminance(s.e); lum.f = Luminance(s.f);
    lum.g = Luminance(s.g); lum.h = Luminance(s.h);
    lum.i = Luminance(s.i); lum.j = Luminance(s.j);
    lum.k = Luminance(s.k); lum.l = Luminance(s.l);
    lum.n = Luminance(s.n); lum.o = Luminance(s.o);

    return lum;
}

// Detect edges using cross gradients of luminance
EdgeInfo DetectEdges(NeighborhoodLuminance lum)
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

// Lanczos 4x4 reconstruction (radius = 2)
float3 LanczosReconstruction(NeighborhoodSamples s, float2 frac)
{
    float radius = 2.0;

    // Compute Lanczos weights for the 4x4 grid
    float wx0 = LanczosWeight(frac.x + 1.0, radius);  // e, i column
    float wx1 = LanczosWeight(frac.x, radius);        // f, j column
    float wx2 = LanczosWeight(frac.x - 1.0, radius);  // g, k column
    float wx3 = LanczosWeight(frac.x - 2.0, radius);  // h, l column

    float wy0 = LanczosWeight(frac.y + 1.0, radius);  // b, c row
    float wy1 = LanczosWeight(frac.y, radius);        // e, f, g, h row
    float wy2 = LanczosWeight(frac.y - 1.0, radius);  // i, j, k, l row
    float wy3 = LanczosWeight(frac.y - 2.0, radius);  // n, o row

    // Normalize weights
    float sumX = wx0 + wx1 + wx2 + wx3;
    float sumY = wy0 + wy1 + wy2 + wy3;
    wx0 /= sumX; wx1 /= sumX; wx2 /= sumX; wx3 /= sumX;
    wy0 /= sumY; wy1 /= sumY; wy2 /= sumY; wy3 /= sumY;

    // Row -1 (only b, c available, use linear for missing samples)
    float w01 = wx1 + wx0 * 0.5;
    float w02 = wx2 + wx3 * 0.5;
    float3 row0 = (s.b * w01 + s.c * w02) / (w01 + w02);

    // Row 0 (e, f, g, h available)
    float3 row1 = s.e * wx0 + s.f * wx1 + s.g * wx2 + s.h * wx3;

    // Row 1 (i, j, k, l available)
    float3 row2 = s.i * wx0 + s.j * wx1 + s.k * wx2 + s.l * wx3;

    // Row 2 (only n, o available, use linear for missing samples)
    float3 row3 = (s.n * w01 + s.o * w02) / (w01 + w02);

    return row0 * wy0 + row1 * wy1 + row2 * wy2 + row3 * wy3;
}

// Edge-aware interpolation refinement
float3 EdgeAwareRefinement(NeighborhoodSamples s, float3 lanczos, float2 frac, EdgeInfo edge)
{
    // Compute edge-aware samples (stretch along edges)
    float3 hBlend = lerp(lerp(s.e, s.f, frac.x), lerp(s.g, s.h, frac.x), 0.5);
    float3 vBlend = lerp(lerp(s.b, s.c, frac.x), lerp(s.n, s.o, frac.x), 0.5);

    // Blend based on edge direction
    float3 edgeAware = lerp(
        lerp(lanczos, hBlend, edge.hWeight * edge.strength * 0.5),
        lerp(lanczos, vBlend, edge.vWeight * edge.strength * 0.5),
        0.5
    );

    // Final blend: use more Lanczos in smooth areas, more edge-aware on edges
    return lerp(lanczos, edgeAware, edge.strength * 0.7);
}

// RCAS-style adaptive sharpening
float3 ApplySharpening(float3 color, NeighborhoodSamples s)
{
    // Compute local contrast
    float3 minColor = min(min(min(s.f, s.g), s.j), s.k);
    float3 maxColor = max(max(max(s.f, s.g), s.j), s.k);
    float3 contrast = maxColor - minColor;

    // Adaptive sharpening - less sharpening in high contrast areas
    float contrastLum = Luminance(contrast);
    float adaptiveSharp = Sharpness * saturate(1.0 - contrastLum * 2.0);

    // Compute sharpening using the bilinear neighborhood
    float3 neighbors = (s.f + s.g + s.j + s.k) * 0.25;
    float3 sharpened = color + (color - neighbors) * adaptiveSharp;

    // Clamp to prevent ringing artifacts
    return clamp(sharpened, minColor, maxColor);
}

// Apply SDF edge overlay at full resolution
float3 ApplyEdgeOverlay(float3 color, float2 uv)
{
    float4 emissive = EmissiveTexture.Sample(Sampler, uv);

    // emissive.a > 0 means we're on a surface - overlay the actual emissive color
    float edgeMask = saturate(emissive.a * 3.0 - EdgeOverlay + 1.0);
    return lerp(color, emissive.rgb, edgeMask);
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

    // Sample neighborhood and compute luminance
    NeighborhoodSamples samples = SampleNeighborhood(baseUV, texelSize);
    NeighborhoodLuminance luminance = ComputeNeighborhoodLuminance(samples);

    // Detect edges
    EdgeInfo edge = DetectEdges(luminance);

    // Lanczos reconstruction
    float3 lanczos = LanczosReconstruction(samples, frac);

    // Edge-aware refinement
    float3 result = EdgeAwareRefinement(samples, lanczos, frac, edge);

    // Optional sharpening
    if (Sharpness > 0.0)
    {
        result = ApplySharpening(result, samples);
    }

    // Optional edge overlay
    if (EdgeOverlay > 0.0)
    {
        result = ApplyEdgeOverlay(result, uv);
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
