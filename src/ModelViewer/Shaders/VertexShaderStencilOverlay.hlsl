/// <summary>
/// Vertex shader for the stencil overlay fullscreen quad.
/// Passes NDC position straight through — no transformation needed.
/// </summary>
struct OverlayVSOutput
{
    float4 Position : SV_POSITION;
};

OverlayVSOutput VSMain(float4 Position : POSITION)
{
    OverlayVSOutput output;
    output.Position = Position;
    return output;
}
