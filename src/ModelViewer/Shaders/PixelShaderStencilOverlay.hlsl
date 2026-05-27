/// <summary>
/// Pixel shader for the stencil overlay. Outputs a solid semi-transparent color.
/// The hardware stencil test (Comparison.Equal) ensures only fragments where
/// the stencil buffer matches the reference value are written to the render target.
/// No stencil texture sampling is needed.
/// </summary>

float4 PSMain(float4 Position : SV_POSITION) : SV_TARGET
{
    // Solid semi-transparent cyan overlay — stencil test handles visibility
    return float4(0.0f, 1.0f, 1.0f, 0.35f);
}
