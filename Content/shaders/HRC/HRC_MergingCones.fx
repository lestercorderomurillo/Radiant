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

	PACKED FORMAT: RGB = radiance, A = transmittance (grayscale)
*/

// t0/s0 reserved for MonoGame SpriteBatch
Texture2D VraysCascade : register(t1);
Texture2D PrevMerge : register(t2);

SamplerState SamplerVrays : register(s1);
SamplerState SamplerPrev : register(s2);

float2 VraysSize;
float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
};

// Merges packed near and far: RGB = radiance, A = transmittance
float4 MergePacked(float4 near, float4 far)
{
    float3 radiance = near.rgb + (far.rgb * near.a);
    float transmit = near.a * far.a;
    return float4(radiance, transmit);
}

// Samples packed radiance+transmit from Vrays texture
// probe is in pixel coordinates, converts to normalized UV for sampling
float4 GetVraysVolume(float2 probe, float index, float interval, float lookupWidth)
{
    // Convert probe position to texel coordinate in Vrays texture
    float planeIndex = floor(probe.x / interval);
    float2 samplePos = float2(planeIndex * lookupWidth + index + 0.5, probe.y + 0.5);

    // Convert to normalized UV coordinates
    float2 uv = samplePos / VraysSize;

    // Bounds check - return transparent black if out of bounds
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    return VraysCascade.Sample(SamplerVrays, uv);
}

// Samples packed radiance+transmit from Prev (merged) texture
// probe is in pixel coordinates, converts to normalized UV for sampling
float4 GetPrevVolume(float2 probe, float index, float interval, float lookupWidth)
{
    // Convert probe position to texel coordinate in Prev texture
    float planeIndex = floor(probe.x / interval);
    float2 samplePos = float2(planeIndex * lookupWidth + index + 0.5, probe.y + 0.5);

    // Convert to normalized UV coordinates
    float2 uv = samplePos / PrevSize;

    // Bounds check - return transparent black if out of bounds
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    return PrevMerge.Sample(SamplerPrev, uv);
}

// Merges a single cone (left or right side) by sampling rays and combining with previous cascade
// Returns packed float4: RGB = radiance, A = transmittance
// side: 0.0 = left cone edge, 1.0 = right cone edge
float4 MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side)
{
    // In HRC, cones are formed by pairs of rays
    // For cascade N, each cone samples 2 rays from cascade N's Vrays
    // and merges with the corresponding cone from the previous merged cascade
    float vrayIndex = index + side;

    // Compute the cone index for the previous merge level
    // Each cone in the current level maps to 2 cones in the next level
    float prevConeIndex = index * 2.0 + side;

    // Sample the current cascade's ray
    float4 vray = GetVraysVolume(probe, vrayIndex, intrv, vrays);

    // Determine alignment offset based on even/odd plane
    // Odd planes: ray endpoints align with next cascade's ray origins
    // Even planes: need interpolation between near/far planes
    float isEven = 1.0 - fmod(plane, 2.0);

    // Compute merge position for sampling previous cascade
    // For odd planes: offset by full interval
    // For even planes: offset by half interval (interpolate)
    float alignmentFactor = isEven == 1.0 ? 1.0 : 2.0;
    float2 mergeOffset = float2(intrv * alignmentFactor, vrayIndex * 2.0 - intrv);
    float2 mergeProbe = probe + mergeOffset;

    // Previous cascade parameters (one level coarser)
    float prevInterval = intrv * 2.0;
    float prevVrays = prevInterval + 1.0;

    // Sample the previously merged cascade at the cone position
    float4 prevCone = GetPrevVolume(mergeProbe, prevConeIndex, prevInterval, prevVrays);

    if (isEven == 1.0)
    {
        // Even planes: interpolate between near and far merge points
        float2 nearProbe = probe;
        float2 farProbe = probe + float2(intrv * 2.0, vrayIndex * 2.0 - intrv);

        float4 nearCone = GetPrevVolume(nearProbe, prevConeIndex, prevInterval, prevVrays);
        float4 farCone = GetPrevVolume(farProbe, prevConeIndex, prevInterval, prevVrays);

        // Merge ray with interpolated cone using standard radiance cascade merge
        float4 interpCone = lerp(nearCone, farCone, 0.5);
        return MergePacked(vray, interpCone);
    }
    else
    {
        // Odd planes: direct merge with aligned cascade
        return MergePacked(vray, prevCone);
    }
}

// Main pixel shader - merges cones from current cascade with previous cascade
// Single render target: RGB = radiance, A = transmittance
float4 MainPS(PixelShaderInput input) : SV_Target0
{
    // Convert UV to texel coordinates in the merge output texture
    float2 texel = input.UV * CascadeSize;

    // Compute cascade parameters
    float cascadeIdx = CascadeIndex.x;
    float interval = pow(2.0, cascadeIdx);
    float virtualRays = interval + 1.0;

    // Determine which probe plane and cone index we're computing
    float plane = floor(texel.x / interval);
    float coneIndex = floor(texel.x - (plane * interval));

    // Probe position in world/pixel space
    float2 probe = float2(plane * interval + 0.5, texel.y);

    // Merge left cone (side = 0.0) and right cone (side = 1.0)
    float4 leftCone = MergeCone(probe, plane, interval, virtualRays, coneIndex, 0.0);
    float4 rightCone = MergeCone(probe, plane, interval, virtualRays, coneIndex, 1.0);

    // Combine left and right cone contributions
    // Sum radiance, multiply transmittance
    float3 totalRadiance = leftCone.rgb + rightCone.rgb;
    float totalTransmit = leftCone.a * rightCone.a;

    return float4(totalRadiance, totalTransmit);
}

technique GenerateOutputTexture
{
    pass P0
    {
        PixelShader = compile ps_5_0 MainPS();
    }
}
