using System.Runtime.InteropServices;
using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Constant buffer data uploaded to the GPU each frame for shadow/lighting.
/// Layout must match the HLSL cbuffer exactly.
/// 
/// HLSL cbuffer (b1) register layout (128 bytes = 8 vector registers):
///   b1[0..3]: matrix LightViewProjection;        // 64 bytes
///   b1[4]:    float3 LightDirection + Padding;   // 16 bytes
///   b1[5]:    float PcfRadius + ShadowBias + pad;// 16 bytes
///   b1[6]:    float3 LightColor + pad;           // 16 bytes
///   b1[7]:    float3 AmbientColor + pad;         // 16 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShadowConstantBuffer
{
    public Matrix LightViewProjection; // 64 bytes
    public Vector3 LightDirection; // 12 bytes
    public float Padding; // 4 bytes (fills b1[4])
    public float PcfRadius; // 4 bytes (b1[5].x)
    public float ShadowBias; // 4 bytes (b1[5].y)
    public float ShadowNormalBias; // 4 Bytes
    public float Padding2; // 4 bytes (fills b1[4])
    public Vector4 LightColor; // 16 bytes (b1[6].xyz)
    public Vector4 AmbientColor; // 16 bytes (b1[7].xyz)

    public ShadowConstantBuffer(Matrix lightViewProj, Vector3 lightDir,
        float pcfRadius = ShadowSettings.DefaultPcfRadius, float shadowBias = ShadowSettings.DefaultShadowBias)
    {
        LightViewProjection = lightViewProj;
        LightDirection = lightDir;
        Padding = 0f;
        PcfRadius = pcfRadius;
        ShadowBias = shadowBias;
        ShadowNormalBias = ShadowSettings.DefaultShadowNormalBias;
        LightColor = ShadowSettings.DefaultLightColor;
        AmbientColor = ShadowSettings.DefaultAmbientColor;
    }
}