//-----------------------------------------------------------------------------
// SMAA — pass 2 of 3: blending weight calculation (pixel stage)
//
// The heaviest SMAA pass: it walks the edge buffer left/right and up/down to
// find edge endpoints, then looks the resulting pattern up in the area texture
// to get subpixel blend weights. Output is written to an RGBA8 target where
// .rg holds the horizontal weights and .ba the vertical ones.
//
// Textures are bound in declaration order by SMAARenderer.cs:
//   t0 = edges   t1 = area lookup   t2 = search lookup
//-----------------------------------------------------------------------------

cbuffer SMAAConstants : register(b0) { float4 SMAARtMetrics; };

#define SMAA_RT_METRICS SMAARtMetrics
#define SMAA_HLSL_4
#define SMAA_INCLUDE_VS 0

#include "SMAA.hlsl"

Texture2D edgesTex;
Texture2D areaTex;
Texture2D searchTex;

// The pixel stage declares SV_POSITION (unused) on the same grounds as
// PixelShaderSMAAEdge.hlsl: without it the runtime mis-maps the vertex
// shader's TEXCOORD outputs.

struct SMAAPsInput
{
    float4 position : SV_POSITION;
    float4 offset0 : TEXCOORD0;
    float4 offset1 : TEXCOORD1;
    float4 offset2 : TEXCOORD2;
};

float4 PSMain(SMAAPsInput input) : SV_TARGET
{
    float4 offset[3];
    offset[0] = input.offset0;
    offset[1] = input.offset1;
    offset[2] = input.offset2;

    // SMAABlendingWeightCalculationVS emits
    //   offset0 = texcoord.xyxy + SMAA_RT_METRICS.xyxy * (-0.25, -0.125, 1.25, -0.125),
    // so the pixel's own coordinate (and the pixel-space coordinate the area
    // lookup needs) is recovered exactly by undoing that sub-texel shift.
    float2 texcoord = float2(input.offset0.x + 0.25 * SMAA_RT_METRICS.x,
                             input.offset0.y + 0.125 * SMAA_RT_METRICS.y);
    float2 pixcoord = texcoord * SMAA_RT_METRICS.zw;

    // 'subsampleIndices' is only used for SMAA with temporal subsampling
    // (SMAA_T), so zero is passed for plain SMAA 1x — see @SUBSAMPLE_INDICES.
    return SMAABlendingWeightCalculationPS(texcoord, pixcoord, offset,
        edgesTex, areaTex, searchTex, float4(0.0, 0.0, 0.0, 0.0));
}
