//-----------------------------------------------------------------------------
// SMAA — pass 3 of 3: neighborhood blending (pixel stage)
//
// Applies the subpixel offsets computed in pass 2 to the scene image. Pixels
// without any blend weight are copied through untouched.
//
// Textures are bound in declaration order by SMAARenderer.cs:
//   t0 = scene (offscreen)   t1 = blend weights
//-----------------------------------------------------------------------------

cbuffer SMAAConstants : register(b0) { float4 SMAARtMetrics; };

#define SMAA_RT_METRICS SMAARtMetrics
#define SMAA_HLSL_4
#define SMAA_INCLUDE_VS 0

#include "SMAA.hlsl"

Texture2D sceneTex;
Texture2D blendTex;

struct SMAAPsInput
{
    float4 position : SV_POSITION;
    float4 offset : TEXCOORD0;
};

float4 PSMain(SMAAPsInput input) : SV_TARGET
{
    // The vertex stage only passes the neighbour offsets, and the pixel stage
    // must declare SV_POSITION for the runtime to map the remaining
    // interpolants correctly (see the note at the top of
    // PixelShaderSMAAEdge.hlsl). SMAANeighborhoodBlendingVS emits
    //   offset = texcoord.xyxy + SMAA_RT_METRICS.xyxy * (1, 0, 0, 1)
    //        = (u + 1/w, v, u, v + 1/h),
    // so .z is the pixel's own u and .y is its own v — reading those two
    // components recovers the coordinate exactly, whereas subtracting a texel
    // from .y (which was never shifted) sampled the scene one row too high.
    float2 texcoord = float2(input.offset.z, input.offset.y);

    return SMAANeighborhoodBlendingPS(texcoord, input.offset, sceneTex, blendTex);
}
