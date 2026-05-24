struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float4 LightClipPos : TEXCOORD2; // ── New: light clip-space position ──
};

float AmbientGradient(float3 normal)
{
    float gradient = dot(normal, float3(0, 1, 0));
    float gradient2 = dot(normal, float3(1, 0, 0));
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
    float3 LightDirection; // Direction pointing TO the light source
    float Padding;
    float PcfRadius; // PCF sample spread in texels (0 = hard shadows)
    float ShadowBias; // Base depth bias to prevent shadow acne
    float ShadowNormalBias;
    float4 LightColor;
    float4 AmbientColor;
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
    return depth <= storedDepth ? 1.0f : 0.0f;
}

// ── Hash-based noise for randomized PCF sampling (ps_4_0 compatible) ──
/// <summary>
/// Returns a pseudo-random value in [0, 1) based on UV coordinates and a seed.
/// Uses sin/cos mixing — fully compatible with shader model 4.0.
/// </summary>
float HashFloat(float2 uv, float seed)
{
    uv += seed;
    float f = dot(uv, float2(12.9898f, 78.233f));
    return frac(sin(f) * 43758.5453f);
}

/// <summary>
/// Returns a pseudo-random 2D offset in [-0.5, 0.5) for a given UV and seed.
/// </summary>
float2 RandomFloat2(float2 uv, float seed)
{
    return float2(HashFloat(uv, seed), HashFloat(uv, seed + 1.0f)) - 0.5f;
}

/// <summary>
/// Computes the soft shadow factor using 9-tap Percentage-Closer Filtering (PCF)
/// with randomized sample offsets for smoother shadow edges.
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

    // ── 9-tap PCF with randomized offsets (Randomized PCF) ──
    // Each sample gets a unique jitter so the grid pattern doesn't alias
    float texelSize = 1.0f / 2048.0f;
    float2 offset = texelSize * PcfRadius;

    float calculatedDepth = ndcPos.z - ShadowBias;

    float shadowSum = 0.0f;

    // ── Unrolled 3×3 kernel with per-sample noise jitter ──
    shadowSum += SampleShadowDepth(uv + float2(-1.0f, -1.0f) * offset + RandomFloat2(uv, 1.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 0.0f, -1.0f) * offset + RandomFloat2(uv, 2.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 1.0f, -1.0f) * offset + RandomFloat2(uv, 3.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2(-1.0f,  0.0f) * offset + RandomFloat2(uv, 4.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 0.0f,  0.0f) * offset + RandomFloat2(uv, 5.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 1.0f,  0.0f) * offset + RandomFloat2(uv, 6.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2(-1.0f,  1.0f) * offset + RandomFloat2(uv, 7.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 0.0f,  1.0f) * offset + RandomFloat2(uv, 8.0f) * offset, calculatedDepth);
    shadowSum += SampleShadowDepth(uv + float2( 1.0f,  1.0f) * offset + RandomFloat2(uv, 9.0f) * offset, calculatedDepth);

    return shadowSum / 9.0f;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    // ── Lighting parameters come from the cbuffer ──
    float3 lightDir = normalize(LightDirection);
    float3 lightColor = LightColor;
    float3 ambientColor = AmbientColor;
    float3 diffuseColor = float3(0.5, 0.5, 0.5);

    float3 normal = normalize(input.WorldNormal);
    ambientColor = ambientColor * AmbientGradient(normal);

    float NdotL = saturate(dot(normal, lightDir));
    float betterLambert = pow(NdotL, 0.5);
    float3 diffuse = lightColor * diffuseColor * betterLambert;

    // ── Apply shadow factor to diffuse lighting ──
    float shadow = ComputeShadowFactor(input.LightClipPos);
    diffuse *= shadow;

    float3 color = ambientColor + diffuse;
    return float4(color, 1.0);
}
