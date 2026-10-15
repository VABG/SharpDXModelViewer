//-----------------------------------------------------------------------------
// SMAA — pass 2 of 3: blending weight calculation (vertex stage)
//
// Same macro setup as the other wrappers — see VertexShaderSMAAEdge.hlsl for
// why SMAA_RT_METRICS / SMAA_HLSL_4 have to be defined before the include.
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

    // NDC → texture space, with the Y axis inverted because D3D's viewport
    // points down (NDC y = +1 is the render target's top row = v 0) —
    // see the note in VertexShaderSMAAEdge.hlsl.
    float2 texcoord = float2(input.position.x * 0.5 + 0.5,
                             0.5 - input.position.y * 0.5);

    float2 pixcoord;
    float4 offset[3];
    SMAABlendingWeightCalculationVS(texcoord, pixcoord, offset);

    output.position = input.position;
    output.offset0 = offset[0];
    output.offset1 = offset[1];
    output.offset2 = offset[2];

    return output;
}
