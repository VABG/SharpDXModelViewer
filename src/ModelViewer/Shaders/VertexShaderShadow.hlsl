// ──────────────────────────────────────────────────────────────
// Depth-pass vertex shader for shadow map rendering.
// Transforms vertices into light-space and outputs only position.
// The depth buffer is written automatically by the rasterizer.
// ──────────────────────────────────────────────────────────────

cbuffer LightMatrices : register(b0)
{
    matrix LightViewProjection;  // Light View × Projection (pre-combined)
    float3 LightDirection;       // Unused in depth pass, but cbuffer layout matches
    float Padding;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal   : NORMAL;
    float2 TexCoord : TEXCOORD;
};

struct VSOutput
{
    float4 Position    : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos    : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
};

VSOutput VSMain(VSInput input)
{
    VSOutput output;
    // Transform world-space position into light clip-space
    float4 worldPos = float4(input.Position, 1.0);
    output.Position = mul(worldPos, LightViewProjection);
    
    output.WorldNormal = input.Normal;
    output.WorldPos = input.Position;
    output.TexCoord = input.TexCoord;
    return output;
}
