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
*/

Texture2D PrevRadiance : register(t0);
Texture2D PrevTransmit : register(t1);

SamplerState Sampler0 : register(s0);

cbuffer Constants : register(b0)
{
    float2 PrevSize;
    float2 CascadeSize;
    float2 CascadeIndex;
};

#define FRUSTUM_COUNT 4.0


// Merges radiance and transmittance from near and far volumes
// Combines light from two volumes: near radiance + far radiance attenuated by near transmittance
void mergeRadiance(float4 nearRadiance, float4 nearTransmit, 
                   float4 farRadiance, float4 farTransmit, 
                   out float4 radiance, out float4 transmit)
{
    radiance = nearRadiance + (farRadiance * nearTransmit);
    transmit = nearTransmit * farTransmit;
}


// Samples radiance and transmittance from textures at a specific volume position
// Returns default values if sample position is at origin (0,0)
void getVolume(float2 probe, float index, float interval, float lookupWidth, float2 resolution,
               Texture2D textureRadiance, Texture2D textureTransmit,
               float4 defaultValueRadiance, float4 defaultValueTransmit,
               out float4 radiance, out float4 transmit)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / resolution;

    // Weight is 1.0 if position is NOT at origin, 0.0 if at origin
    float weight = (floor(samplePos.x) != 0.0 || floor(samplePos.y) != 0.0) ? 0.0 : 1.0;

    radiance = lerp(textureRadiance.Sample(Sampler0, samplePos), defaultValueRadiance, weight);
    transmit = lerp(textureTransmit.Sample(Sampler0, samplePos), defaultValueTransmit, weight);
}


// Extends a ray by merging two rays from the previous cascade level
// Creates new ray by combining near and far rays from lower resolution cascade
void extendRay(float2 probe, float lowerIndex, float higherIndex, 
               float previousInterval, float previousVirtualRays,
               out float4 radiance, out float4 transmit)
{
    float2 mergePosition = probe + float2(previousInterval, -previousInterval + (lowerIndex * 2.0));
    
    float4 radianceNear, transmitNear, radianceFar, transmitFar;
    
    getVolume(probe, lowerIndex, previousInterval, previousVirtualRays, PrevSize,
              PrevRadiance, PrevTransmit,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              radianceNear, transmitNear);

    getVolume(mergePosition, higherIndex, previousInterval, previousVirtualRays, PrevSize,
              PrevRadiance, PrevTransmit,
              float4(0.0, 0.0, 0.0, 0.0), float4(1.0, 1.0, 1.0, 1.0),
              radianceFar, transmitFar);
              
    mergeRadiance(radianceNear, transmitNear, radianceFar, transmitFar, radiance, transmit);
}


// Main pixel shader - computes cascaded volumetric light propagation
// Extends rays from previous cascade level by merging diverging ray pairs
void main(float2 texelCoord : TEXCOORD0,
          out float4 outRadiance : SV_Target0,
          out float4 outTransmit : SV_Target1)
{
    float2 texel = texelCoord * CascadeSize;
    float interval = pow(2.0, CascadeIndex.x);
    float virtualRays = interval + 1.0;
    float plane = floor(texel.x / virtualRays);
    float index = floor(texel.x - (plane * virtualRays));
    float2 probe = float2(plane * interval, texel.y) + float2(0.5, 0.0);
    
    float previousInterval = pow(2.0, CascadeIndex.x - 1.0);
    float previousVirtualRays = previousInterval + 1.0;
    
    float lowerIndex = floor(index * 0.5);
    float upperIndex = ceil(index * 0.5);
    
    float4 radianceLeft, radianceUpper, transmitLeft, transmitUpper;
    
    extendRay(probe, lowerIndex, upperIndex, previousInterval, previousVirtualRays, 
              radianceLeft, transmitLeft);
              
    extendRay(probe, upperIndex, lowerIndex, previousInterval, previousVirtualRays, 
              radianceUpper, transmitUpper);
    
    outRadiance = lerp(radianceLeft, radianceUpper, 0.5);
    outTransmit = lerp(transmitLeft, transmitUpper, 0.5);
}