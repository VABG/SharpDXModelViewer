//-----------------------------------------------------------------------------
// SMAA — pass 1 of 3: edge detection (vertex stage)
//
// Thin wrapper around the reference 'SMAA.hlsl' implementation so that it can be
// compiled by ShaderCompiler with the project's usual entry points ("VSMain" /
// "PSMain") and profiles ("vs_4_0" / "ps_4_0").
//
// The reference header cannot work out these macros on its own, so they are
// provided here:
//   • SMAA_RT_METRICS — float4(1/width, 1/height, width, height). The header has
//     no default for it, so it is bound to the 'SMAAConstants' constant buffer
//     (slot b0), which SMAARenderer.cs uploads once per resize.
//   • SMAA_HLSL_4     — selects the Direct3D 11 texture / sampler syntax.
//   • SMAA_INCLUDE_PS — the pixel-stage code is not needed by a vertex shader.
//
// The constant buffer must be declared before the include because the header's
// function bodies expand SMAA_RT_METRICS into 'SMAARtMetrics'.
//-----------------------------------------------------------------------------

cbuffer SMAAConstants : register(b0) { float4 SMAARtMetrics; };

#define SMAA_RT_METRICS SMAARtMetrics
#define SMAA_HLSL_4
#define SMAA_INCLUDE_PS 0

#include "SMAA.hlsl"

struct SMAAVsInput
{
    float4 position : POSITION;
};

struct SMAAVsOutput
{
    float4 position : SV_POSITION;
    float4 offset0 : TEXCOORD0;
    float4 offset1 : TEXCOORD1;
    float4 offset2 : TEXCOORD2;
};

SMAAVsOutput VSMain(SMAAVsInput input)
{
    SMAAVsOutput output;

    // Fullscreen quad in NDC space → texture space.
    //
    // D3D's viewport Y axis points down: NDC y = +1 lands on the render
    // target's top row (row 0), and that is where texture coordinate v = 0 is.
    // So the Y half of the mapping must be inverted — a plain
    // 'position.xy * 0.5 + 0.5' reads every screen-sized texture upside down
    // (and mirrors SMAA's 'top' / 'bottom' neighbour offsets).
    float2 texcoord = float2(input.position.x * 0.5 + 0.5,
                             0.5 - input.position.y * 0.5);

    float4 offset[3];
    SMAAEdgeDetectionVS(texcoord, offset);

    output.position = input.position;
    output.offset0 = offset[0];
    output.offset1 = offset[1];
    output.offset2 = offset[2];

    return output;
}
