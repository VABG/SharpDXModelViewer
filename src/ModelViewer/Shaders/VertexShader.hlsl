cbuffer ViewProjection : register(b0)
{
    matrix View;
    matrix Projection;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal   : NORMAL;
    float2 TexCoord : TEXCOORD;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos    : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
};

VSOutput VSMain(VSInput input)
{
    VSOutput output;
    float4 worldPos = float4(input.Position, 1.0);
    output.Position = mul(worldPos, View);
    output.Position = mul(output.Position, Projection);
    output.WorldNormal = input.Normal;
    output.WorldPos = input.Position;
    output.TexCoord = input.TexCoord;
    return output;
}

