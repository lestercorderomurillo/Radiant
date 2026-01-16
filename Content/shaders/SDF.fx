static const float PI = 3.14159265359;
static const float EPSILON = 0.00001;

Texture2D EmissiveTexture : register(t0);
Texture2D JFATexture : register(t1);

SamplerState SceneColorSampler : register(s0);
SamplerState JFASampler : register(s1);

float2 WorldsBounds;
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
    return color.a > 0.0;
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

float4 InitializeJFAPS(PixelShaderInput input) : COLOR
{
    if (IsSurface(input.UV))
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

float4 GenerateSDFFromJFAPS(PixelShaderInput input) : COLOR
{
    if (IsSurface(input.UV))
        return float4(0.0, 0.0, 0.0, 1.0);
    
    bool hasSurface;
    float2 closestUV = DecodePositionPacked(JFATexture.Sample(JFASampler, input.UV), hasSurface);
    
    if (!hasSurface)
        return float4(1.0, 0.0, 0.0, 1.0);
    
    float2 deltaUV = closestUV - input.UV;
    float2 deltaSDF = deltaUV * WorldsBounds;
    
    float sdfScale = ScreenDiagonal / length(WorldsBounds);
    float screenDistance = length(deltaSDF) * sdfScale;
    float normalizedDistance = saturate(screenDistance / ScreenDiagonal);
    
    return float4(normalizedDistance, 0.0, 0.0, 1.0);
}

float4 DebugJFAPS(PixelShaderInput input) : COLOR
{
    bool hasSurface;
    float2 closestUV = DecodePositionPacked(JFATexture.Sample(JFASampler, input.UV), hasSurface);
    
    if (!hasSurface)
        return float4(0.0, 0.0, 0.0, 1.0);
    
    float2 deltaUV = closestUV - input.UV;
    float2 deltaPixels = deltaUV * WorldsBounds;
    
    if (dot(deltaPixels, deltaPixels) < 0.5)
        return float4(1.0, 1.0, 0.0, 1.0);
    
    float2 direction = normalize(deltaPixels);
    return float4(direction * 0.5 + 0.5, 0.0, 1.0);
}

float4 DebugJFARawPS(PixelShaderInput input) : COLOR
{
    bool hasSurface;
    float2 storedUV = DecodePositionPacked(JFATexture.Sample(JFASampler, input.UV), hasSurface);
    
    if (!hasSurface)
        return float4(0.0, 0.0, 0.0, 1.0);
    
    return float4(storedUV.x, storedUV.y, 0.5, 1.0);
}

float4 DebugSDFVisiblePS(PixelShaderInput input) : COLOR
{
    if (IsSurface(input.UV))
        return float4(1.0, 1.0, 0.0, 1.0);
    
    bool hasSurface;
    float2 closestUV = DecodePositionPacked(JFATexture.Sample(JFASampler, input.UV), hasSurface);
    
    if (!hasSurface)
        return float4(0.0, 0.0, 0.0, 1.0);
    
    float2 deltaUV = closestUV - input.UV;
    float2 deltaSDF = deltaUV * WorldsBounds;
    
    float sdfScale = ScreenDiagonal / length(WorldsBounds);
    float screenDistance = length(deltaSDF) * sdfScale;
    float v = saturate(screenDistance / (ScreenDiagonal * 0.5));
    
    return float4(v, v, v, 1.0);
}

float4 DebugEmissivePS(PixelShaderInput input) : COLOR
{
    return EmissiveTexture.Sample(SceneColorSampler, input.UV);
}

technique InitializeJFA
{
    pass P0 
    { 
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 InitializeJFAPS(); 
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