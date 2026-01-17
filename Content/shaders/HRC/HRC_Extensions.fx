/*
	For each ray of cN compute the ray-extension from 4 smaller left and right rays of cN-1.

		R /\ L
		L \/ R

	Two rays of cN-1 diverge and then converge when their directions are swapped.
	For the left ray extent the L -> R ray(s) indices at the near/far planes.
	For the right ray extent the R -> L ray(s) indices at the near/far planes.
	Interpolate the final result to converge back to the extended cN ray direction.

	Even ray indices will have the same left/right ray indices as their directions
	in cN will be the same as the lower cascade cN-1.

	PACKED FORMAT: RGB = radiance, A = transmittance (grayscale)
*/

// t0/s0 reserved for MonoGame SpriteBatch
Texture2D PrevCascade : register(t1);
SamplerState SamplerPrev : register(s1);

float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
};

// Merges packed radiance+transmit from near and far volumes
// RGB = radiance, A = transmittance
float4 MergePacked(float4 near, float4 far)
{
    float3 radiance = near.rgb + (far.rgb * near.a);
    float transmit = near.a * far.a;
    return float4(radiance, transmit);
}

// Samples packed radiance+transmit from texture at a specific volume position
float4 GetVolume(float2 probe, float index, float interval, float lookupWidth)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / PrevSize;

    // Clamp to valid UV range and sample
    samplePos = saturate(samplePos);
    return PrevCascade.Sample(SamplerPrev, samplePos);
}

// Extends a ray by merging two rays from the previous cascade level
float4 ExtendRay(float2 probe, float lowerIndex, float higherIndex,
                 float previousInterval, float previousVirtualRays)
{
    float2 mergePosition = probe + float2(previousInterval, -previousInterval + (lowerIndex * 2.0));

    float4 near = GetVolume(probe, lowerIndex, previousInterval, previousVirtualRays);
    float4 far = GetVolume(mergePosition, higherIndex, previousInterval, previousVirtualRays);

    return MergePacked(near, far);
}

// Main pixel shader - computes cascaded volumetric light propagation
// Single render target: RGB = radiance, A = transmittance
float4 MainPS(PixelShaderInput input) : SV_Target0
{
    float2 texel = input.UV * CascadeSize;
    float interval = pow(2.0, CascadeIndex.x);
    float virtualRays = interval + 1.0;
    float plane = floor(texel.x / virtualRays);
    float index = floor(texel.x - (plane * virtualRays));
    float2 probe = float2(plane * interval, texel.y) + float2(0.5, 0.0);

    float previousInterval = pow(2.0, CascadeIndex.x - 1.0);
    float previousVirtualRays = previousInterval + 1.0;

    float lowerIndex = floor(index * 0.5);
    float upperIndex = ceil(index * 0.5);

    float4 left = ExtendRay(probe, lowerIndex, upperIndex, previousInterval, previousVirtualRays);
    float4 right = ExtendRay(probe, upperIndex, lowerIndex, previousInterval, previousVirtualRays);

    return lerp(left, right, 0.5);
}

technique GenerateOutputTexture
{
    pass P0
    {
        PixelShader = compile ps_5_0 MainPS();
    }
}
