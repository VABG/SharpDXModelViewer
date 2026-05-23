using System.Runtime.InteropServices;
using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Constant buffer data uploaded to the GPU each frame for shadow/lighting.
/// Layout must match the HLSL cbuffer exactly.
/// 
/// HLSL cbuffer (b1):
///   matrix LightViewProjection;  // 64 bytes (4x16 floats)
///   float3 LightDirection;       // 12 bytes
///   float Padding;              // 4 bytes (to align to 16-byte boundary)
///   float PcfRadius;            // 4 bytes (PCF soft shadow radius in texels)
///   float ShadowBias;           // 4 bytes (depth bias to prevent shadow acne)
///   = 80 bytes total (5 floats in the second vector register slot)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ShadowConstantBuffer
{
    public Matrix LightViewProjection;   // 64 bytes
    public Vector3 LightDirection;       // 12 bytes
    public float Padding;               // 4 bytes (alignment)
    public float PcfRadius;             // 4 bytes (0 = hard shadows, ~2-4 = soft)
    public float ShadowBias;            // 4 bytes (typically 0.001 - 0.005)
    public float _pad0;                // 4 bytes (pad to 16-byte boundary)
    public float _pad1;                // 4 bytes (pad to 16-byte boundary)

    public ShadowConstantBuffer(Matrix lightViewProj, Vector3 lightDir,
        float pcfRadius = 2.0f, float shadowBias = 0.002f)
    {
        LightViewProjection = lightViewProj;
        LightDirection = lightDir;
        Padding = 0f;
        PcfRadius = pcfRadius;
        ShadowBias = shadowBias;
        _pad0 = 0f;
        _pad1 = 0f;
    }
}
