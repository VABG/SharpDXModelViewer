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
///   = 80 bytes total (2 full 16-byte registers + 1 partial = 3 registers = 48 bytes min, padded to 64)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ShadowConstantBuffer
{
    public Matrix LightViewProjection;   // 64 bytes
    public Vector3 LightDirection;       // 12 bytes
    public float Padding;               // 4 bytes (alignment)

    public ShadowConstantBuffer(Matrix lightViewProj, Vector3 lightDir)
    {
        LightViewProjection = lightViewProj;
        LightDirection = lightDir;
        Padding = 0f;
    }
}
