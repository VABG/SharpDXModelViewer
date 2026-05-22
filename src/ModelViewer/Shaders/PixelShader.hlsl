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
    float gradient2 = dot(normal,float3(0,0,1));
    gradient *= 0.5;
    gradient += 0.5;
    gradient2 *= 0.5;
    gradient2 += 0.5;
    gradient = lerp(gradient, gradient2, 0.25);
    return gradient;
}

// ── Shadow map texture (bound at t0 during main scene pass) ──
Texture2D ShadowMapTexture : register(t0);

// Plain point sampler — we read depth with .Sample() and compare manually
SamplerState ShadowSampler : register(s0)
{
    Filter = FILTER_MIN_MAG_MIP_POINT;
};

/// <summary>
/// Computes the shadow factor for a fragment.
/// Returns 1.0 = fully lit, 0.0 = fully shadowed.
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

        // ── Read the stored depth at this UV ──
    float storedDepth = ShadowMapTexture.Sample(ShadowSampler, uv).r;

    // ── Compare with small bias to prevent shadow acne ──
    float shadowBias = 0.001;
    float shadowFactor = ndcPos.z <= storedDepth + shadowBias ? 1.0f : 0.0f;

    return shadowFactor;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    // ── Light direction points TO the light source (above the scene) ──
    // ShadowMap uses (0.577, -0.577, 0.577) as ray direction (downward),
    // so lighting direction is the opposite (upward toward the light).
    float3 lightDir = normalize(float3(-0.577, 0.577, -0.577));
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

