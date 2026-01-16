/*
	Merging in HRC does not work like merging in Vanilla RC.
	We have two different merging strategies, one for even-index planes and one for odd-index planes.
	Ray-Endpoints of odd-planes perfectly align with Ray-Startpoints of the nearest cN+1 plane.
	However this is not the case for even planes, so we must compute the merging at the closest near
	and far planes, compute merge results for both, then interpolate their fluence to get the
	final merge result for the non-existent plane that even-planes need to merge with.

	The general case merging strategy is that we must compute this current cone of cN by sampling
	the rays at the cone's left/right edges and merging each ray with its respective cN+1 cone.
	We then add the merge result of both the merged left/right rays to compute the cone's fluence.
*/

Texture2D VraysRadiance : register(t0);
Texture2D VraysTransmit : register(t1);
Texture2D PrevRadiance : register(t2);
Texture2D PrevTransmit : register(t3);

SamplerState Sampler0 : register(s0);

cbuffer Constants : register(b0)
{
    float2 VraysSize;
    float2 PrevSize;
    float2 CascadeSize;
    float2 CascadeIndex;
};

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

// Merges near and far radiance/transmittance using standard compositing
// nearR + (farR * nearT) accumulates radiance, nearT * farT accumulates occlusion
void MergeRadiance(float4 nearR, float4 nearT, float4 farR, float4 farT,
                   out float4 radiance, out float4 transmit)
{
    radiance = nearR + (farR * nearT);
    transmit = nearT * farT;
}

// Samples radiance and transmittance from a volume at the specified probe location
// Returns default values if sample position is out of bounds
void GetVolume(float2 probe, float index, float interval, float lookupWidth,
               float2 resolution, Texture2D txtR, Texture2D txtT,
               float4 defValR, float4 defValT,
               out float4 rad, out float4 trn)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    float weight = float(floor(samplePos.x) != 0.0 || floor(samplePos.y) != 0.0);
    rad = lerp(txtR.Sample(Sampler0, samplePos), defValR, weight);
    trn = lerp(txtT.Sample(Sampler0, samplePos), defValT, weight);
}

// Merges a single cone (left or right side) by sampling rays and combining with previous cascade
// Even planes require interpolation between near/far results, odd planes merge directly
void MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side,
               out float4 radiance, out float4 transmit)
{
    float coneI = index * 2.0 + side;
    float vrayI = index + side;
    float2 limit = float2(intrv, -intrv);
    float align = 2.0 - fmod(plane, 2.0);

    float2 merge = probe + align * (limit + float2(0.0, vrayI * 2.0));
    float2 vrayLL = (limit * 2.0) + float2(0.0, (coneI * 2.0));
    float2 vrayRR = (limit * 2.0) + float2(0.0, (coneI + 1.0) * 2.0);
    float coneW = atan(vrayRR.y / vrayRR.x) - atan(vrayLL.y / vrayLL.x);

    float4 vrayR, vrayT, coneFarR, coneFarT;
    GetVolume(probe, vrayI, intrv, vrays, VraysSize, VraysRadiance, VraysTransmit,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0), vrayR, vrayT);
    GetVolume(merge, coneI, 1.0, 1.0, PrevSize, PrevRadiance, PrevTransmit,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0), coneFarR, coneFarT);

    if (fmod(plane, 2.0) == 0.0)
    {
        // Even planes: interpolate between near and far merge results
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float4 vrayR_Ext, vrayT_Ext, coneNearR, coneNearT;
        GetVolume(probeFar, vrayI, intrv, vrays, VraysSize, VraysRadiance, VraysTransmit,
                  float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0), vrayR_Ext, vrayT_Ext);
        GetVolume(probeNear, coneI, 1.0, 1.0, PrevSize, PrevRadiance, PrevTransmit,
                  float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0), coneNearR, coneNearT);

        MergeRadiance(vrayR, vrayT, vrayR_Ext, vrayT_Ext, vrayR, vrayT);
        MergeRadiance(vrayR * coneW, vrayT, coneFarR, coneFarT, radiance, transmit);

        radiance = lerp(radiance, coneNearR, 0.5);
        transmit = lerp(transmit, coneNearT, 0.5);
    }
    else
    {
        // Odd planes: direct merge with aligned cascade
        radiance = (vrayR * coneW) + (coneFarR * vrayT);
        transmit = vrayT * coneFarT;
    }
}

// Main pixel shader - merges cones from current cascade with previous cascade
// Computes left and right cone contributions and combines them
void MainPS(float2 texelCoord : TEXCOORD0,
            out float4 outRadiance : SV_Target0,
            out float4 outTransmit : SV_Target1)
{
    float2 texel = texelCoord * CascadeSize;
    float intrv = pow(2.0, CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / intrv);
    float index = floor(texel.x - (plane * intrv));
    float2 probe = float2(plane * intrv, texel.y) + float2(0.5, 0.0);

    float4 radL, radR, trnL, trnR;
    MergeCone(probe, plane, intrv, vrays, index, 0.0, radL, trnL);
    MergeCone(probe, plane, intrv, vrays, index, 1.0, radR, trnR);

    outRadiance = radL + radR;
    outTransmit = trnL + trnR;
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}
