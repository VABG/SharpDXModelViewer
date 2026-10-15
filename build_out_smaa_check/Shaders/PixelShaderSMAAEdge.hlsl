//-----------------------------------------------------------------------------
// SMAA — pass 1 of 3: edge detection (pixel stage)
//
// Writes the detected horizontal/vertical edges into the .rg channels of the
// edges render target (see SMAARenderer.cs, which allocates it as RGBA8).
//
// Luma edge detection is used because the scene is rendered into a plain
// R8G8B8A8_UNORM target whose values are already in display space — see the
// note in SMAA.hlsl about 'colorTex' needing to be gamma-corrected.
//-----------------------------------------------------------------------------

// NOTE: the pixel stage must declare SV_POSITION even though it does not use
// it.  Without it the D3D11 runtime mis-maps the first TEXCOORD interpolant of
// the pixel shader against the vertex shader's outputs (the vertex shader's
// SV_POSITION output has no counterpart), which silently delivers (0,0) for
// that interpolant and shifts the neighbour offsets by one row.

cbuffer SMAAConstants : register(b0) { float4 SMAARtMetrics; };

#define SMAA_RT_METRICS SMAARtMetrics
#define SMAA_HLSL_4
#define SMAA_INCLUDE_VS 0

#include "SMAA.hlsl"

// Bound at t0 by SMAARenderer — the offscreen scene (the same image that gets
// resolved in the third pass).
Texture2D sceneTex;

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

    // SMAA's edge vertex shader only passes the three neighbour offsets, so the
    // texture coordinate is recovered from offset0 = texcoord +
    // SMAA_RT_METRICS.xyxy * (-1, 0, 0, -1) = (u - 1/w, v, u, v - 1/h).
    //
    // Note that only .x and .w carry a texel shift: .y is the pixel's own v and
    // .z is its own u, so reading those two components recovers the coordinate
    // exactly (adding metrics.y back to .y would push the centre sample one row
    // down, which is what made the AA land off by one pixel).
    float2 texcoord = float2(input.offset0.z, input.offset0.y);

    float2 edges = SMAALumaEdgeDetectionPS(texcoord, offset, sceneTex);

    return float4(edges, 0.0, 0.0);
}
