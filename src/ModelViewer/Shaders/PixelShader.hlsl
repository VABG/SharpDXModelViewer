struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos    : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
    float4 LightClipPos : TEXCOORD2;  // ── New: light clip-space position ──
};

float AmbientGradient(float3 normal)
{
    float gradient = dot(normal,float3(0,1,0));
    float gradient2 = dot(normal,float3(1,0,0));
    gradient *= 0.5;
    gradient += 0.5;
    gradient2 *= 0.5;
    gradient2 += 0.5;
    gradient = lerp(gradient, gradient2, 0.25);
    gradient += .25;
    return saturate(gradient);
}

// ── Shadow map texture (bound at t0 during main scene pass) ──
Texture2D ShadowMapTexture : register(t0);

// Plain point sampler — we read depth with .Sample() and compare manually
SamplerState ShadowSampler : register(s0)
{
    Filter = FILTER_MIN_MAG_MIP_POINT;
};

// ── Shadow/lighting data (bound at b1 during main scene pass) ──
cbuffer ShadowMatrices : register(b1)
{
    matrix LightViewProjection;
    float3 LightDirection;       // Direction pointing TO the light source
    float Padding;
    float PcfRadius;            // PCF sample spread in texels (0 = hard shadows)
    float ShadowBias;           // Base depth bias to prevent shadow acne
};

/// <summary>
/// Tests a single shadow map sample against the fragment depth.
/// Returns 1.0 if the fragment is lit, 0.0 if it is in shadow.
/// </summary>
float SampleShadowDepth(float2 uv, float depth)
{
    // Clamp UV to prevent edge artifacts when PCF samples spill outside the map
    uv = saturate(uv);
    float storedDepth = ShadowMapTexture.Sample(ShadowSampler, uv).r;
    return depth <= storedDepth + ShadowBias ? 1.0f : 0.0f;
}

/// <summary>
/// Computes the soft shadow factor using 9-tap Percentage-Closer Filtering (PCF).
/// Returns 1.0 = fully lit, 0.0 = fully shadowed.
/// When PcfRadius == 0 the function falls back to a single-sample hard shadow.
/// </summary>
float ComputeShadowFactor(float4 lightClipPos)
{
    // ── Perspective divide to get NDC coordinates ──
    float3 ndcPos = lightClipPos.xyz / lightClipPos.w;

    // ── Transform from NDC [-1,1] to texture space [0,1] ──
    // Flip Y because D3D11 NDC has Y=-1 at bottom but texture UV has Y=0 at top
    float2 uv = ndcPos.xy * 0.5 + 0.5;
    uv.y = 1.0 - uv.y;

    // ── Early out: if outside the shadow map frustum, fragment is fully lit ──
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0f;

    // ── Hard shadow fallback when radius is zero ──
    if (PcfRadius <= 0.0f)
        return SampleShadowDepth(uv, ndcPos.z);

    // ── 9-tap PCF: sample a 3×3 grid around the center UV ──
    float texelSize = 1.0f / 2048.0f;
    float2 offset = texelSize * PcfRadius;

    float shadowSum = 0.0f;
    shadowSum += SampleShadowDepth(uv + float2(-offset.x, -offset.y), ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2( 0.0f,    -offset.y), ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2( offset.x, -offset.y), ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2(-offset.x,  0.0f),    ndcPos.z);
    shadowSum += SampleShadowDepth(uv, ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2( offset.x,  0.0f),    ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2(-offset.x,  offset.y), ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2( 0.0f,     offset.y), ndcPos.z);
    shadowSum += SampleShadowDepth(uv + float2( offset.x,  offset.y), ndcPos.z);

    return shadowSum / 9.0f;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    // ── Light direction comes from the cbuffer (matches ShadowMap) ──
    float3 lightDir = normalize(LightDirection);
    float3 lightColor = float3(1.0, 0.95, 0.9);
    float3 ambientColor = float3(0.15, 0.15, 0.18);
    float3 diffuseColor = float3(0.5, 0.5, 0.5);

    float3 normal = normalize(input.WorldNormal);
    ambientColor = ambientColor * AmbientGradient(normal);

    float NdotL = max(dot(normal, lightDir), 0.0);
    float3 diffuse = lightColor * diffuseColor * NdotL;

    // ── Apply shadow factor to diffuse lighting ──
    float shadow = ComputeShadowFactor(input.LightClipPos);
    diffuse *= shadow;

    float3 color = ambientColor + diffuse;
    return float4(color, 1.0);
}

