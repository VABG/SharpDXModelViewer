// ──────────────────────────────────────────────────────────────
// Depth-pass pixel shader for shadow map rendering.
// Returns early for every pixel — the depth buffer is written
// automatically by the GPU. We don't need to output color.
// ──────────────────────────────────────────────────────────────

struct VSOutput
{
    float4 Position    : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos    : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
};

void PSMain(VSOutput input)
{
    // Early-out: no color output needed.
    // The depth value is written to the shadow map texture automatically.
    return;
}
