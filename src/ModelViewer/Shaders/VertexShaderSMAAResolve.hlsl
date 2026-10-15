//-----------------------------------------------------------------------------
// SMAA — pass 3 of 3: neighborhood blending (vertex stage)
//
// Same macro setup as the other wrappers — see VertexShaderSMAAEdge.hlsl.
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
    float4 offset : TEXCOORD0;
};

SMAAVsOutput VSMain(SMAAVsInput input)
{
    SMAAVsOutput output;

    // NDC → texture space, with the Y axis inverted because D3D's viewport
    // points down (NDC y = +1 is the render target's top row = v 0) —
    // see the note in VertexShaderSMAAEdge.hlsl.
    float2 texcoord = float2(input.position.x * 0.5 + 0.5,
                             0.5 - input.position.y * 0.5);

    output.position = input.position;
    SMAANeighborhoodBlendingVS(texcoord, output.offset);

    return output;
}
