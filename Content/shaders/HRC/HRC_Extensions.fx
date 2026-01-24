/* HRC_Extensions.fx
   Ray extension combines chained rays from cascade N-1 to form rays of cascade N.
   Transmittance is packed in the alpha channel. */

Texture2D PrevCascade : register(t0);  // RGB = radiance, A = transmittance

SamplerState SamplerPrev : register(s0);

float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;
float ProbeScale;

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

struct PixelShaderOutput
{
    float4 Radiance : SV_Target0;  // RGB = radiance, A = transmittance
};

// Merge radiance with single-channel transmittance in alpha
float4 MergeRadiance(float4 near, float4 far)
{
    // radiance = nearR + farR * nearT
    // transmit = nearT * farT
    return float4(near.rgb + far.rgb * near.a, near.a * far.a);
}

float4 GetVolume(float2 probe, float index, float interval, float lookupWidth,
                 float2 resolution, float4 defVal)
{
    // probe.y is in world space, scale down for half-height texture
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    return lerp(PrevCascade.Sample(SamplerPrev, samplePos), defVal, weight);
}

float4 ExtendRay(float2 probe, float loIndex, float hiIndex,
                 float prevIntrv, float prevVrays)
{
    float2 merge = probe + float2(prevIntrv, -prevIntrv + (loIndex * 2.0));

    float4 near = GetVolume(probe, loIndex, prevIntrv, prevVrays, PrevSize, float4(0.0, 0.0, 0.0, 1.0));
    float4 far = GetVolume(merge, hiIndex, prevIntrv, prevVrays, PrevSize, float4(0.0, 0.0, 0.0, 1.0));

    return MergeRadiance(near, far);
}

PixelShaderOutput MainPS(PixelShaderInput input)
{
    PixelShaderOutput output;

    float2 texel = input.UV * CascadeSize;
    float intrv = exp2(CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / vrays);
    float index = floor(texel.x - (plane * vrays));
    float2 probe = float2(plane * intrv, texel.y * ProbeScale) + float2(0.5, 0.0);

    float prevIntrv = exp2(CascadeIndex.x - 1.0);
    float prevVrays = prevIntrv + 1.0;

    float lower = floor(index * 0.5);
    float upper = ceil(index * 0.5);

    float4 resultL = ExtendRay(probe, lower, upper, prevIntrv, prevVrays);
    float4 resultU = ExtendRay(probe, upper, lower, prevIntrv, prevVrays);

    output.Radiance = lerp(resultL, resultU, 0.5);
    return output;
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}

float2 FlipX(float2 uv)
{
    return float2(1.0 - uv.x, uv.y);
}

float2 FlipY(float2 uv)
{
    return float2(uv.x, 1.0 - uv.y);
}