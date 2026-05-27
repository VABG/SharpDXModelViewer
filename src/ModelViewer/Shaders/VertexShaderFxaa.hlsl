// Vertex shader for the FXAA fullscreen pass.
// Takes NDC position from the vertex buffer and generates UVs.

struct FxaaVSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

FxaaVSOutput VSMain(float4 Position : POSITION)
{
    FxaaVSOutput output;
    output.Position = Position;
    // Map NDC [-1,+1] → UV [0,1]
    output.TexCoord = Position.xy * 0.5f + 0.5f;
    return output;
}

