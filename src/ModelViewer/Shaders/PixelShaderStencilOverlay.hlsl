/// <summary>
/// Pixel shader for the stencil overlay. Reads the stencil buffer and
/// highlights pixels that match the selected model's stencil ID (0x42)
/// with a semi-transparent cyan outline. All other pixels are fully
/// transparent so the scene underneath shows through.
/// </summary>

// Stencil ID used by StencilSelectionRenderer
static const uint STENCIL_ID = 0x42;

// Stencil depth texture bound at t0 — we read the stencil channel (.y in R32G8X24,
// or directly as R8_UInt in the SRV format used by DeviceManager).
Texture2D StencilTexture : register(t0);
SamplerState StencilSampler : register(s0);

float4 PSMain(float4 Position : SV_POSITION) : SV_TARGET
{
    // Convert NDC [-1,1] to UV [0,1]
    float2 uv = Position.xy * 0.5 + 0.5;
    // Flip Y because NDC Y goes bottom→top but texture UV goes top→bottom
    uv.y = 1.0 - uv.y;

    // Read stencil value
    uint stencil = StencilTexture.Sample(StencilSampler, uv).x;

    // If this pixel belongs to the selected model, draw a cyan highlight
    if (stencil == STENCIL_ID)
    {
        return float4(0.0f, 1.0f, 1.0f, 0.35f); // semi-transparent cyan
    }

    // Otherwise fully transparent — scene shows through
    return float4(0.0f, 0.0f, 0.0f, 0.0f);
}
