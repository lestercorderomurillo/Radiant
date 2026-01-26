static const float PI = 3.14159265359;
static const float EPSILON = 0.00001;

Texture2D EmissiveTexture : register(t0);
Texture2D JFATexture : register(t1);
Texture2D JFATextureInterior : register(t2);
Texture2D MotionVectorTexture : register(t3);

SamplerState SceneColorSampler : register(s0);
SamplerState JFASampler : register(s1);

float2 WorldsBounds;
float2 JFASize;
float ScreenDiagonal;
float JumpDistance;

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = float4(input.Position, 1.0);
    output.UV = input.TexCoord;
    return output;
}

bool IsSurface(float2 uv)
{
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return false;
    float4 color = EmissiveTexture.Sample(SceneColorSampler, uv);
    return color.a > EPSILON;
}

// Get alpha value for subpixel distance refinement
float GetSurfaceAlpha(float2 uv)
{
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 0.0;
    return EmissiveTexture.Sample(SceneColorSampler, uv).a;
}

float4 EncodePositionPacked(float2 uv, bool hasSurface)
{
    if (!hasSurface)
        return float4(0.0, 0.0, 0.0, 0.0);
    return float4(uv.x, uv.y, 0.0, 1.0);
}

float2 DecodePositionPacked(float4 encoded, out bool hasSurface)
{
    hasSurface = encoded.a > 0.5;
    return float2(encoded.x, encoded.y);
}

float UVDistanceSq(float2 uv1, float2 uv2)
{
    float2 deltaPixels = (uv1 - uv2) * WorldsBounds;
    return dot(deltaPixels, deltaPixels);
}

// Initialize exterior JFA: seed surface pixels, flood outward
float4 InitializeJFAPS(PixelShaderInput input) : COLOR
{
    if (IsSurface(input.UV))
        return EncodePositionPacked(input.UV, true);
    return float4(0.0, 0.0, 0.0, 0.0);
}

// Initialize interior JFA: seed non-surface pixels, flood inward
float4 InitializeJFAInteriorPS(PixelShaderInput input) : COLOR
{
    if (!IsSurface(input.UV))
        return EncodePositionPacked(input.UV, true);
    return float4(0.0, 0.0, 0.0, 0.0);
}

float4 JFAPassPS(PixelShaderInput input) : COLOR
{
    float2 texelSize = 1.0 / WorldsBounds;
    
    bool hasSurface;
    float4 currentData = JFATexture.Sample(JFASampler, input.UV);
    float2 storedUV = DecodePositionPacked(currentData, hasSurface);
    
    float2 closestUV = storedUV;
    float minDistanceSq = hasSurface ? UVDistanceSq(storedUV, input.UV) : 999999.0;
    bool foundSurface = hasSurface;
    
    float2 offsets[8] = {
        float2(-1, -1), float2(0, -1), float2(1, -1),
        float2(-1,  0),                float2(1,  0),
        float2(-1,  1), float2(0,  1), float2(1,  1)
    };
    
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 neighborUV = input.UV + offsets[i] * JumpDistance * texelSize;
        
        if (neighborUV.x < 0.0 || neighborUV.x > 1.0 || 
            neighborUV.y < 0.0 || neighborUV.y > 1.0)
            continue;
        
        bool neighborHasSurface;
        float2 neighborStoredUV = DecodePositionPacked(JFATexture.Sample(JFASampler, neighborUV), neighborHasSurface);
        
        if (neighborHasSurface)
        {
            float distSq = UVDistanceSq(neighborStoredUV, input.UV);

            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;
                closestUV = neighborStoredUV;
                foundSurface = true;
            }
        }
    }
    
    return EncodePositionPacked(closestUV, foundSurface);
}

// Sample 2x2 JFA texels and return minimum distance (fixes Voronoi boundary artifacts at lower resolution)
float SampleJFAMinDistance(Texture2D jfaTex, float2 uv, float2 currentPixelUV)
{
    // Convert UV to JFA texel coordinate
    float2 texelCoordF = uv * JFASize;

    // Find base texel (the 4 texels whose centers surround this point)
    int2 baseTexel = int2(floor(texelCoordF - 0.5));

    float minDist = 999999.0;

    // Sample 2x2 neighborhood
    [unroll]
    for (int y = 0; y <= 1; y++)
    {
        [unroll]
        for (int x = 0; x <= 1; x++)
        {
            int2 texel = baseTexel + int2(x, y);

            // Clamp to valid range
            texel = clamp(texel, int2(0, 0), int2(JFASize) - 1);

            // Load exact texel value (no filtering)
            float4 data = jfaTex.Load(int3(texel, 0));

            bool hasSeed;
            float2 seedUV = DecodePositionPacked(data, hasSeed);

            if (hasSeed)
            {
                float2 delta = (seedUV - currentPixelUV) * WorldsBounds;
                float dist = length(delta);
                minDist = min(minDist, dist);
            }
        }
    }

    return minDist;
}

// Signed SDF: -1 to 1, where 0 = surface boundary
// Negative = inside geometry, Positive = outside geometry
// Uses alpha for subpixel distance refinement
float4 GenerateSDFFromJFAPS(PixelShaderInput input) : COLOR
{
    float alpha = GetSurfaceAlpha(input.UV);
    bool isInside = IsSurface(input.UV);

    // Subpixel offset based on alpha gradient (alpha 0.5 = on boundary)
    // alpha 1.0 = ~0.5 pixels inside, alpha 0.0 = ~0.5 pixels outside
    float subpixelOffset = (alpha - 0.5);

    if (isInside)
    {
        // Inside geometry - use interior JFA to find distance to boundary (negative)
        float pixelDistance = SampleJFAMinDistance(JFATextureInterior, input.UV, input.UV);

        if (pixelDistance > 999998.0)
            return float4(-1.0, 0.0, 0.0, 1.0); // Deep inside, max negative

        // Refine with subpixel offset (more alpha = further inside)
        pixelDistance = max(0.0, pixelDistance + subpixelOffset);

        float normalizedDistance = saturate(pixelDistance / ScreenDiagonal);

        if (normalizedDistance < EPSILON)
            return float4(0.0, 0.0, 0.0, 1.0); // On boundary

        return float4(-normalizedDistance, 0.0, 0.0, 1.0); // Negative = inside
    }
    else
    {
        // Outside geometry - use exterior JFA to find distance to surface (positive)
        float pixelDistance = SampleJFAMinDistance(JFATexture, input.UV, input.UV);

        if (pixelDistance > 999998.0)
            return float4(1.0, 0.0, 0.0, 1.0); // Far outside, max positive

        // Refine with subpixel offset (less alpha = further outside)
        pixelDistance = max(0.0, pixelDistance - subpixelOffset);

        float normalizedDistance = saturate(pixelDistance / ScreenDiagonal);

        return float4(normalizedDistance, 0.0, 0.0, 1.0); // Positive = outside
    }
}

// Convert angle to rainbow color (hue wheel)
float3 AngleToColor(float angle)
{
    float h = angle / (2.0 * PI); // 0-1
    float3 rgb = saturate(abs(fmod(h * 6.0 + float3(0, 4, 2), 6.0) - 3.0) - 1.0);
    return rgb;
}

// Sample JFA with multi-sample and return closest seed UV and distance
float2 SampleJFAClosestSeed(Texture2D jfaTex, float2 uv, float2 currentPixelUV, out float outDist)
{
    float2 texelCoordF = uv * JFASize;
    int2 baseTexel = int2(floor(texelCoordF - 0.5));

    float minDist = 999999.0;
    float2 bestSeed = float2(0, 0);

    [unroll]
    for (int y = 0; y <= 1; y++)
    {
        [unroll]
        for (int x = 0; x <= 1; x++)
        {
            int2 texel = baseTexel + int2(x, y);
            texel = clamp(texel, int2(0, 0), int2(JFASize) - 1);

            float4 data = jfaTex.Load(int3(texel, 0));
            bool hasSeed;
            float2 seedUV = DecodePositionPacked(data, hasSeed);

            if (hasSeed)
            {
                float2 delta = (seedUV - currentPixelUV) * WorldsBounds;
                float dist = length(delta);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestSeed = seedUV;
                }
            }
        }
    }

    outDist = minDist;
    return bestSeed;
}

// JFA Direction debug: shows direction to nearest boundary as color wheel + distance as brightness
float4 DebugJFAPS(PixelShaderInput input) : COLOR
{
    bool isInside = IsSurface(input.UV);
    float dist;
    float2 closestUV;

    if (isInside)
        closestUV = SampleJFAClosestSeed(JFATextureInterior, input.UV, input.UV, dist);
    else
        closestUV = SampleJFAClosestSeed(JFATexture, input.UV, input.UV, dist);

    if (dist > 999998.0)
        return float4(0.1, 0.1, 0.1, 1.0); // Dark gray = no data

    // Boundary = yellow
    if (dist < 2.0)
        return float4(1.0, 1.0, 0.0, 1.0);

    // Direction as angle -> rainbow color
    float2 deltaPixels = (closestUV - input.UV) * WorldsBounds;
    float angle = atan2(deltaPixels.y, deltaPixels.x) + PI; // 0 to 2PI
    float3 dirColor = AngleToColor(angle);

    // Brightness based on distance (bright near, dim far)
    float brightness = 1.0 - saturate(dist / 150.0) * 0.6;

    // Tint inside slightly blue, outside slightly warm
    if (isInside)
        dirColor = lerp(dirColor, float3(0.3, 0.5, 1.0), 0.2);
    else
        dirColor = lerp(dirColor, float3(1.0, 0.7, 0.4), 0.15);

    return float4(dirColor * brightness, 1.0);
}

float4 DebugJFARawPS(PixelShaderInput input) : COLOR
{
    bool hasSurface;
    float2 storedUV = DecodePositionPacked(JFATexture.Sample(JFASampler, input.UV), hasSurface);
    
    if (!hasSurface)
        return float4(0.0, 0.0, 0.0, 1.0);
    
    return float4(storedUV.x, storedUV.y, 0.5, 1.0);
}

// Debug signed SDF: Cyan->Blue = inside, Yellow = boundary, Orange->Red = outside
float4 DebugSDFVisiblePS(PixelShaderInput input) : COLOR
{
    bool isInside = IsSurface(input.UV);

    if (isInside)
    {
        // Inside geometry
        float pixelDist = SampleJFAMinDistance(JFATextureInterior, input.UV, input.UV);

        if (pixelDist > 999998.0)
            return float4(1.0, 0.0, 1.0, 1.0); // Magenta = ERROR: no exterior found

        if (pixelDist < 1.5)
            return float4(1.0, 1.0, 0.0, 1.0); // Yellow = boundary

        // Cyan (near) -> Blue (far)
        float t = saturate(pixelDist / 50.0);
        return float4(0.0, 1.0 - t, 1.0, 1.0);
    }
    else
    {
        // Outside geometry
        float pixelDist = SampleJFAMinDistance(JFATexture, input.UV, input.UV);

        if (pixelDist > 999998.0)
            return float4(0.2, 0.0, 0.0, 1.0); // Dark red = no surface

        // Orange (near) -> Red (far)
        float t = saturate(pixelDist / 50.0);
        return float4(1.0, 0.5 * (1.0 - t), 0.0, 1.0);
    }
}

float4 DebugEmissivePS(PixelShaderInput input) : COLOR
{
    return EmissiveTexture.Sample(SceneColorSampler, input.UV);
}

// Arrow SDF: returns distance to arrow shape pointing in +X direction
float ArrowSDF(float2 p, float arrowLen, float headSize)
{
    // Arrow body (line from origin to arrowLen)
    float bodyLen = arrowLen - headSize;
    float2 bodyEnd = float2(bodyLen, 0);

    // Distance to line segment (body)
    float2 pa = p;
    float2 ba = bodyEnd;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    float bodyDist = length(pa - ba * h) - 0.5;  // 0.5 = line thickness

    // Arrow head (triangle)
    float2 ph = p - float2(bodyLen, 0);
    float headDist = max(
        -ph.x,  // Left of head start
        max(
            ph.y - headSize * 0.5 + ph.x * 0.5,   // Top edge
            -ph.y - headSize * 0.5 + ph.x * 0.5   // Bottom edge
        )
    );
    headDist = max(headDist, ph.x - headSize);  // Right of tip

    return min(bodyDist, headDist);
}

// Rotate point by angle
float2 RotatePoint(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(p.x * c - p.y * s, p.x * s + p.y * c);
}

// Sample motion and return velocity with highest speed from 4 neighborhood samples
float2 SampleStrongestMotion(float2 center, float2 bounds)
{
    static const float SAMPLE_OFFSET = 6.0;  // Offset from center for 4 samples

    float2 offsets[4] = {
        float2(-SAMPLE_OFFSET, -SAMPLE_OFFSET),
        float2( SAMPLE_OFFSET, -SAMPLE_OFFSET),
        float2(-SAMPLE_OFFSET,  SAMPLE_OFFSET),
        float2( SAMPLE_OFFSET,  SAMPLE_OFFSET)
    };

    float2 bestVelocity = float2(0, 0);
    float bestSpeed = 0;

    // 127/255 - Color(0.5f) truncates to byte 127, not 128
    static const float BYTE_CENTER = 127.0 / 255.0;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        int2 texel = int2(center + offsets[i]);
        texel = clamp(texel, int2(0, 0), int2(bounds) - 1);
        float2 motion = MotionVectorTexture.Load(int3(texel, 0)).rg;
        float2 vel = (motion - BYTE_CENTER) * 200.0;
        float spd = length(vel);

        if (spd > bestSpeed)
        {
            bestSpeed = spd;
            bestVelocity = vel;
        }
    }

    return bestVelocity;
}

// Debug motion vectors: arrows showing direction and magnitude
float4 DebugMotionVectorsPS(PixelShaderInput input) : COLOR
{
    static const float CELL_SIZE = 32.0;
    static const float GRID_WIDTH = 1.0;

    float2 pixelPos = input.UV * WorldsBounds;
    float2 cellIndex = floor(pixelPos / CELL_SIZE);
    float2 cellCenter = (cellIndex + 0.5) * CELL_SIZE;
    float2 localPos = pixelPos - cellCenter;

    float3 bgColor = float3(0.02, 0.02, 0.02);
    float3 gridColor = float3(0.08, 0.08, 0.08);

    // Draw grid lines
    float2 cellPos = fmod(pixelPos, CELL_SIZE);
    bool onGrid = cellPos.x < GRID_WIDTH || cellPos.y < GRID_WIDTH;

    // Sample strongest motion from 4 neighborhood points
    float2 velocity = SampleStrongestMotion(cellCenter, WorldsBounds);
    float speed = length(velocity);

    // No motion: draw small white dot in center
    if (speed < 0.1)
    {
        float dotDist = length(localPos);
        if (dotDist < 2.0)
            return float4(0.4, 0.4, 0.4, 1.0);  // White dot
        return float4(onGrid ? gridColor : bgColor, 1.0);
    }

    // Arrow direction (screen coords, Y+ is down)
    float angle = atan2(velocity.y, velocity.x);
    // Bigger arrows: increased multiplier
    float arrowLength = min(sqrt(speed) * 10.0, CELL_SIZE * 0.85);
    float headSize = arrowLength * 0.45;

    float2 rotatedPos = RotatePoint(localPos, -angle);
    rotatedPos.x += arrowLength * 0.35;

    float dist = ArrowSDF(rotatedPos, arrowLength, headSize);

    float3 arrowColor = AngleToColor(angle + PI);
    float brightness = 0.7 + saturate(speed / 10.0) * 0.3;
    arrowColor *= brightness;

    float alpha = 1.0 - smoothstep(-1.0, 1.0, dist);

    float3 baseColor = onGrid ? gridColor : bgColor;
    return float4(lerp(baseColor, arrowColor, alpha), 1.0);
}

technique InitializeJFA
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 InitializeJFAPS();
    }
}

technique InitializeJFAInterior
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 InitializeJFAInteriorPS();
    }
}

technique JFAPass
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 JFAPassPS(); 
    }
}

technique GenerateSDFFromJFA
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 GenerateSDFFromJFAPS(); 
    }
}

technique DebugSDFVisible
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 DebugSDFVisiblePS(); 
    }
}

technique DebugJFARaw
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 DebugJFARawPS(); 
    }
}

technique DebugJFA
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 DebugJFAPS(); 
    }
}

technique DebugEmissive
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 DebugEmissivePS();
    }
}

technique DebugMotionVectors
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 DebugMotionVectorsPS();
    }
}

// Clear motion vector texture to 127/255 (matches Color byte precision)
// Color(0.5f) truncates: 0.5*255=127.5 -> byte 127, not 128
static const float MV_CENTER = 127.0 / 255.0;

float4 ClearMotionPS(PixelShaderInput input) : COLOR
{
    return float4(MV_CENTER, MV_CENTER, 0.0, 1.0);
}

technique ClearMotion
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 ClearMotionPS();
    }
}