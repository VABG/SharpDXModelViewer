// FXAA (Fast Approximate Anti-Aliasing) pixel shader.
// Based on NVIDIA FXAA 3.11 by Timothy Lottes.
// 
// FXAA works by detecting luminance edges and performing directional
// blur along the edge to reduce aliasing artifacts.  It is a single-pass
// screen-space post-process that runs very efficiently on all hardware.

// ── Input texture from the scene render target ──
Texture2D SceneTexture : register(t0);
SamplerState SceneSampler : register(s0);

// ── FXAA tuning parameters ──
cbuffer FxaaSettings : register(b0)
{
    // Subpixel blending amount (0.0 = off, 0.25 = max)
    float FxaaSubpix;
    // Minimum luminance difference to consider an edge
    float FxaaEdgeThresholdMin;
    // Maximum luminance difference before clamping
    float FxaaEdgeThresholdMax;
    // Maximum search iterations
    float FxaaMaxIterations;
    // Reciprocal of render target dimensions
    float2 FxaaPixelThreshold; // = 1.0 / (width, height)
    float4 _padding;
};

/// <summary>
/// Computes luminance from an RGB color.
/// Uses the standard Rec. 709 luminance weights.
/// </summary>
float FxaaLuminance(float3 color)
{
    return dot(color, float3(0.299f, 0.587f, 0.114f));
}

/// <summary>
/// Samples the scene texture and returns luminance.
/// </summary>
float FxaaLuminanceAt(float2 uv)
{
    float3 color = SceneTexture.Sample(SceneSampler, uv).rgb;
    return FxaaLuminance(color);
}

/// <summary>
/// FXAA 3.11 main entry point.
/// </summary>
float4 PSMain(float2 texCoord : TEXCOORD0) : SV_TARGET
{
    float2 fxaaPxThreshold = FxaaPixelThreshold;
    float subpix = FxaaSubpix;
    float edgeThresholdMin = FxaaEdgeThresholdMin;
    float edgeThresholdMax = FxaaEdgeThresholdMax;

    // ── Sample center and compute luminance ──
    float3 rgbCenter = SceneTexture.Sample(SceneSampler, texCoord).rgb;
    float lCenter = FxaaLuminance(rgbCenter);

    // ── Sample neighbors for edge detection ──
    float lTop    = FxaaLuminanceAt(texCoord + float2(0.0f, -fxaaPxThreshold.y));
    float lBottom = FxaaLuminanceAt(texCoord + float2(0.0f,  fxaaPxThreshold.y));
    float lLeft   = FxaaLuminanceAt(texCoord + float2(-fxaaPxThreshold.x, 0.0f));
    float lRight  = FxaaLuminanceAt(texCoord + float2( fxaaPxThreshold.x, 0.0f));

    float lMin = min(lCenter, min(min(lTop, lBottom), min(lLeft, lRight)));
    float lMax = max(lCenter, max(max(lTop, lBottom), max(lLeft, lRight)));

    // ── Early exit if luminance range is too small ──
    float lRange = lMax - lMin;
    if (lRange < max(edgeThresholdMin, lMax * edgeThresholdMax))
        return float4(rgbCenter, 1.0f);

    // ── Compute horizontal and vertical gradient ──
    float lGradientH = abs(lLeft  - lRight) * 2.0f + abs(lCenter - lTop) + abs(lCenter - lBottom);
    float lGradientV = abs(lTop   - lBottom) * 2.0f + abs(lCenter - lLeft) + abs(lCenter - lRight);

    // ── Determine if edge is horizontal or vertical ──
    float isHorizontal = lGradientH > lGradientV ? 1.0f : 0.0f;

    // ── Determine the direction of the gradient ──
    float lGradientH1 = abs(lLeft  - lCenter);
    float lGradientH2 = abs(lRight - lCenter);
    float lGradientV1 = abs(lTop   - lCenter);
    float lGradientV2 = abs(lBottom - lCenter);

    float2 dirOffset;
    float dirReduceMul;
    float lStep;
    float lMask;

    if (isHorizontal != 0.0f)
    {
        dirOffset = float2(fxaaPxThreshold.x, 0.0f);
        dirReduceMul = 1.0f / lGradientH;
        lStep = (lGradientH1 > lGradientH2) ? -lGradientH1 : lGradientH2;
        lMask = (lGradientH1 > lGradientH2) ? lGradientH1 : lGradientH2;
    }
    else
    {
        dirOffset = float2(0.0f, fxaaPxThreshold.y);
        dirReduceMul = 1.0f / lGradientV;
        lStep = (lGradientV1 > lGradientV2) ? -lGradientV1 : lGradientV2;
        lMask = (lGradientV1 > lGradientV2) ? lGradientV1 : lGradientV2;
    }

    // ── Determine initial offset direction ──
    float2 dir;
    if (isHorizontal != 0.0f)
    {
        float lCurrent = (lGradientH1 > lGradientH2) ? lTop : lBottom;
        dir = float2(0.0f, (lCurrent > lCenter) ? -1.0f : 1.0f);
    }
    else
    {
        float lCurrent = (lGradientV1 > lGradientV2) ? lLeft : lRight;
        dir = float2((lCurrent > lCenter) ? -1.0f : 1.0f, 0.0f);
    }

    // ── First search: find the edge in the gradient direction ──
    float2 pos = texCoord;
    pos += dir * dirOffset * -0.5f;

    // Step along the direction sampling luminance until we find the edge
    float2 dir1 = dir * dirOffset;
    float2 dir2 = dir * dirOffset * 2.0f;

    // First search pass — get to the edge
    float2 pos1 = pos + dir1 * -1.0f;
    float2 pos2 = pos + dir1 *  1.0f;
    float lEnd1 = FxaaLuminanceAt(pos1);
    float lEnd2 = FxaaLuminanceAt(pos2);

    float lDelta1 = abs(lEnd1 - lCenter);
    float lDelta2 = abs(lEnd2 - lCenter);

    float lThreshold = lMask * 0.5f;

    bool done1 = lDelta1 >= lThreshold;
    bool done2 = lDelta2 >= lThreshold;

    if (!done1)
    {
        pos1 += dir1 * -1.0f;
        lEnd1 = FxaaLuminanceAt(pos1);
        lDelta1 = abs(lEnd1 - lCenter);
        done1 = lDelta1 >= lThreshold;
    }
    if (!done2)
    {
        pos2 += dir1 *  1.0f;
        lEnd2 = FxaaLuminanceAt(pos2);
        lDelta2 = abs(lEnd2 - lCenter);
        done2 = lDelta2 >= lThreshold;
    }

    // ── Bilinear interpolation between the two edge positions ──
    float2 posFinal;
    if ((pos1.x + pos1.y) > (pos2.x + pos2.y))
    {
        posFinal = pos1;
    }
    else
    {
        posFinal = pos2;
    }

    // ── Second search: refine position ──
    float2 dirReduce = dirOffset * dirReduceMul;

    pos1 = pos1 - dir1;
    pos2 = pos2 + dir1;

    float lSearch1 = FxaaLuminanceAt(pos1);
    float lSearch2 = FxaaLuminanceAt(pos2);

    float lSearchRange1 = abs(lSearch1 - lCenter);
    float lSearchRange2 = abs(lSearch2 - lCenter);

    float lSearchDone1 = lSearchRange1 >= lThreshold;
    float lSearchDone2 = lSearchRange2 >= lThreshold;

    if (!lSearchDone1)
    {
        pos1 -= dirReduce;
        lSearch1 = FxaaLuminanceAt(pos1);
        lSearchRange1 = abs(lSearch1 - lCenter);
        lSearchDone1 = lSearchRange1 >= lThreshold;
    }
    if (!lSearchDone2)
    {
        pos2 += dirReduce;
        lSearch2 = FxaaLuminanceAt(pos2);
        lSearchRange2 = abs(lSearch2 - lCenter);
        lSearchDone2 = lSearchRange2 >= lThreshold;
    }

    if ((pos1.x + pos1.y) > (pos2.x + pos2.y))
        posFinal = pos1;
    else
        posFinal = pos2;

    // ── Compute the perpendicular offset for subpixel AA ──
    float2 posPerp1;
    float2 posPerp2;
    if (isHorizontal != 0.0f)
    {
        posPerp1 = float2(posFinal.x - dirOffset.x * 0.5f, posFinal.y);
        posPerp2 = float2(posFinal.x + dirOffset.x * 0.5f, posFinal.y);
    }
    else
    {
        posPerp1 = float2(posFinal.x, posFinal.y - dirOffset.y * 0.5f);
        posPerp2 = float2(posFinal.x, posFinal.y + dirOffset.y * 0.5f);
    }

    float3 rgb1 = SceneTexture.Sample(SceneSampler, posPerp1).rgb;
    float3 rgb2 = SceneTexture.Sample(SceneSampler, posPerp2).rgb;

    float lPerp1 = FxaaLuminance(rgb1);
    float lPerp2 = FxaaLuminance(rgb2);

    float lPerpRange1 = abs(lPerp1 - lCenter);
    float lPerpRange2 = abs(lPerp2 - lCenter);

    float lPerpRangeMax = max(lPerpRange1, lPerpRange2);

    // ── Subpixel mixing ──
    float mixAmount = lPerpRangeMax / lRange * subpix;
    mixAmount = clamp(mixAmount, 0.0f, 1.0f);

    // ── Final bilinear interpolation ──
    float3 rgbFinal = lerp(
        SceneTexture.Sample(SceneSampler, posFinal).rgb,
        (rgb1 + rgb2) * 0.5f,
        mixAmount);

    return float4(rgbFinal, 1.0f);
}
