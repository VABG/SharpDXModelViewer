cbuffer ViewProjection : register(b0)
{
    matrix View;
    matrix Projection;
};

// ── Shadow light matrices + direction (bound at b1 during main scene pass) ──
cbuffer ShadowMatrices : register(b1)
{
    matrix LightViewProjection;  // Light View × Projection (ortho)
    float3 LightDirection;       // Direction pointing TO the light source
    float Padding;               // Alignment padding
};

// ── Per-object world transform (bound at b2 for each draw call) ──
cbuffer WorldMatrix : register(b2)
{
    matrix World;
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
    // ── New: position in light clip-space for shadow mapping ──
    float4 LightClipPos : TEXCOORD2;
};

VSOutput VSMain(VSInput input)
{
    VSOutput output;

    // ── Transform to world space using per-object world matrix ──
    float4 worldPos = mul(float4(input.Position, 1.0), World);

    // Transform normal by upper-left 3×3 of world matrix (assumes uniform scale)
    output.WorldNormal = mul(input.Normal, (float3x3)World);

    // ── Camera space (existing) ──
    output.Position = mul(worldPos, View);
    output.Position = mul(output.Position, Projection);

    // ── Light space (new) ──
    output.LightClipPos = mul(worldPos, LightViewProjection);

    output.WorldPos = worldPos.xyz;
    output.TexCoord = input.TexCoord;
    return output;
}


