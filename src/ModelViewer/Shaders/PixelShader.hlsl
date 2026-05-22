struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos    : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
};

float AmbientGradient(float3 normal)
{
    float gradient = dot(normal,float3(0,1,0));
    gradient *= 0.5;
    gradient += 0.5;
    return gradient;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    float3 lightDir = normalize(float3(0.577, 0.25, 0.577));
    float3 lightColor = float3(1.0, 0.95, 0.9);
    float3 ambientColor = float3(0.15, 0.15, 0.18);
    float3 diffuseColor = float3(0.5, 0.5, 0.5);

    ;
    
    float3 normal = normalize(input.WorldNormal);
    ambientColor = ambientColor * AmbientGradient(normal);
    
    float NdotL = max(dot(normal, lightDir), 0.0);
    float3 diffuse = lightColor * diffuseColor * NdotL;

    float3 color = ambientColor + diffuse;
    return float4(color, 1.0);
}
