// FSR-style Edge Adaptive Spatial Upsampling (EASU)
// Simplified implementation inspired by AMD FidelityFX FSR 1.0

Texture2D InputTexture : register(t0);
SamplerState Sampler : register(s0);

float2 InputSize;
float2 OutputSize;
float Sharpness;

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

// EASU: Edge Adaptive Spatial Upsampling with Lanczos reconstruction
float4 EASU_PS(PixelShaderInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texelSize = 1.0 / InputSize;
    float2 outputTexelSize = 1.0 / OutputSize;

    // Calculate position in input texture space
    float2 inputPos = uv * InputSize - 0.5;
    float2 inputPosFloor = floor(inputPos);
    float2 frac = inputPos - inputPosFloor;

    // Sample 12-tap pattern (3x4 or 4x3 neighborhood)
    // This provides better edge detection than simple bilinear
    //
    //    b c
    //  e f g h
    //  i j k l
    //    n o

    float2 baseUV = (inputPosFloor + 0.5) * texelSize;

    // Row offsets
    float3 b = InputTexture.Sample(Sampler, baseUV + float2( 0, -1) * texelSize).rgb;
    float3 c = InputTexture.Sample(Sampler, baseUV + float2( 1, -1) * texelSize).rgb;

    float3 e = InputTexture.Sample(Sampler, baseUV + float2(-1,  0) * texelSize).rgb;
    float3 f = InputTexture.Sample(Sampler, baseUV + float2( 0,  0) * texelSize).rgb;
    float3 g = InputTexture.Sample(Sampler, baseUV + float2( 1,  0) * texelSize).rgb;
    float3 h = InputTexture.Sample(Sampler, baseUV + float2( 2,  0) * texelSize).rgb;

    float3 i = InputTexture.Sample(Sampler, baseUV + float2(-1,  1) * texelSize).rgb;
    float3 j = InputTexture.Sample(Sampler, baseUV + float2( 0,  1) * texelSize).rgb;
    float3 k = InputTexture.Sample(Sampler, baseUV + float2( 1,  1) * texelSize).rgb;
    float3 l = InputTexture.Sample(Sampler, baseUV + float2( 2,  1) * texelSize).rgb;

    float3 n = InputTexture.Sample(Sampler, baseUV + float2( 0,  2) * texelSize).rgb;
    float3 o = InputTexture.Sample(Sampler, baseUV + float2( 1,  2) * texelSize).rgb;

    // Compute luminance for edge detection
    float lumB = Luminance(b); float lumC = Luminance(c);
    float lumE = Luminance(e); float lumF = Luminance(f);
    float lumG = Luminance(g); float lumH = Luminance(h);
    float lumI = Luminance(i); float lumJ = Luminance(j);
    float lumK = Luminance(k); float lumL = Luminance(l);
    float lumN = Luminance(n); float lumO = Luminance(o);

    // Detect edges using cross gradients
    // Horizontal edge detection
    float edgeH1 = abs(lumE - lumF) + abs(lumF - lumG) + abs(lumG - lumH);
    float edgeH2 = abs(lumI - lumJ) + abs(lumJ - lumK) + abs(lumK - lumL);
    float edgeH = edgeH1 + edgeH2;

    // Vertical edge detection
    float edgeV1 = abs(lumB - lumF) + abs(lumF - lumJ) + abs(lumJ - lumN);
    float edgeV2 = abs(lumC - lumG) + abs(lumG - lumK) + abs(lumK - lumO);
    float edgeV = edgeV1 + edgeV2;

    // Diagonal edge detection
    float edgeD1 = abs(lumE - lumF) + abs(lumF - lumK) + abs(lumK - lumL);
    float edgeD2 = abs(lumH - lumG) + abs(lumG - lumJ) + abs(lumJ - lumI);

    // Determine dominant edge direction
    float edgeTotal = edgeH + edgeV + 0.0001;
    float hWeight = edgeV / edgeTotal; // More vertical edges = use horizontal interpolation
    float vWeight = edgeH / edgeTotal; // More horizontal edges = use vertical interpolation

    // Edge strength determines blend between Lanczos and edge-aware
    float edgeStrength = saturate((edgeH + edgeV) * 4.0);

    // ============================================
    // Lanczos 4x4 reconstruction (radius = 2)
    // ============================================
    float radius = 2.0;

    // Compute Lanczos weights for the 4x4 grid
    // X weights: for columns at -1, 0, 1, 2 relative to floor position
    float wx0 = LanczosWeight(frac.x + 1.0, radius);  // e, i column
    float wx1 = LanczosWeight(frac.x, radius);        // f, j column
    float wx2 = LanczosWeight(frac.x - 1.0, radius);  // g, k column
    float wx3 = LanczosWeight(frac.x - 2.0, radius);  // h, l column

    // Y weights: for rows at -1, 0, 1, 2 relative to floor position
    float wy0 = LanczosWeight(frac.y + 1.0, radius);  // b, c row
    float wy1 = LanczosWeight(frac.y, radius);        // e, f, g, h row
    float wy2 = LanczosWeight(frac.y - 1.0, radius);  // i, j, k, l row
    float wy3 = LanczosWeight(frac.y - 2.0, radius);  // n, o row

    // Normalize weights
    float sumX = wx0 + wx1 + wx2 + wx3;
    float sumY = wy0 + wy1 + wy2 + wy3;
    wx0 /= sumX; wx1 /= sumX; wx2 /= sumX; wx3 /= sumX;
    wy0 /= sumY; wy1 /= sumY; wy2 /= sumY; wy3 /= sumY;

    // Lanczos interpolation using available samples
    // Row -1 (only b, c available, use linear for missing samples)
    float3 row0 = b * (wx1 + wx0 * 0.5) + c * (wx2 + wx3 * 0.5);
    row0 /= (wx1 + wx0 * 0.5 + wx2 + wx3 * 0.5);

    // Row 0 (e, f, g, h available)
    float3 row1 = e * wx0 + f * wx1 + g * wx2 + h * wx3;

    // Row 1 (i, j, k, l available)
    float3 row2 = i * wx0 + j * wx1 + k * wx2 + l * wx3;

    // Row 2 (only n, o available, use linear for missing samples)
    float3 row3 = n * (wx1 + wx0 * 0.5) + o * (wx2 + wx3 * 0.5);
    row3 /= (wx1 + wx0 * 0.5 + wx2 + wx3 * 0.5);

    // Final Lanczos result
    float3 lanczos = row0 * wy0 + row1 * wy1 + row2 * wy2 + row3 * wy3;

    // ============================================
    // Edge-aware refinement
    // ============================================
    float2 w = frac;

    // Compute edge-aware samples (stretch along edges)
    float3 hBlend = lerp(lerp(e, f, w.x), lerp(g, h, w.x), 0.5);
    float3 vBlend = lerp(lerp(b, c, w.x), lerp(n, o, w.x), 0.5);

    // Blend based on edge direction
    float3 edgeAware = lerp(
        lerp(lanczos, hBlend, hWeight * edgeStrength * 0.5),
        lerp(lanczos, vBlend, vWeight * edgeStrength * 0.5),
        0.5
    );

    // Final blend: use more Lanczos in smooth areas, more edge-aware on edges
    float3 result = lerp(lanczos, edgeAware, edgeStrength * 0.7);

    // RCAS-style sharpening pass
    if (Sharpness > 0.0)
    {
        // Compute local contrast
        float3 minColor = min(min(min(f, g), j), k);
        float3 maxColor = max(max(max(f, g), j), k);
        float3 contrast = maxColor - minColor;

        // Adaptive sharpening - less sharpening in high contrast areas
        float contrastLum = Luminance(contrast);
        float adaptiveSharp = Sharpness * saturate(1.0 - contrastLum * 2.0);

        // Compute sharpening using the bilinear neighborhood
        float3 neighbors = (f + g + j + k) * 0.25;
        float3 sharpened = result + (result - neighbors) * adaptiveSharp;

        // Clamp to prevent ringing artifacts
        result = clamp(sharpened, minColor, maxColor);
    }

    return float4(result, 1.0);
}

technique Upscale
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 EASU_PS();
    }
}
