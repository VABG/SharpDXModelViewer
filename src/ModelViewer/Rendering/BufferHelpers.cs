using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace ModelViewer.Rendering;

/// <summary>
/// Shared helpers for creating immutable vertex and index buffers from
/// managed collections.  Used by both <see cref="Model"/> and <see cref="Grid"/>
/// to eliminate duplicated Marshal / DataStream boilerplate.
/// </summary>
internal static class BufferHelpers
{
    /// <summary>
    /// Creates an immutable vertex buffer from a list of vertices.
    /// </summary>
    internal static Buffer CreateVertexBuffer(
        Device device,
        List<VertexPositionNormalTexture> vertices)
    {
        var vertexDesc = new BufferDescription
        {
            SizeInBytes = vertices.Count * VertexPositionNormalTexture.SizeInBytes,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };

        var vertexSize = vertices.Count * VertexPositionNormalTexture.SizeInBytes;
        var vertexPtr = Marshal.AllocHGlobal(vertexSize);

        try
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                Marshal.StructureToPtr(
                    vertices[i],
                    IntPtr.Add(vertexPtr, i * VertexPositionNormalTexture.SizeInBytes),
                    false);
            }

            using var vertexStream = new DataStream(vertexPtr, vertexSize, true, true);
            return new Buffer(device, vertexStream, vertexDesc);
        }
        finally
        {
            Marshal.FreeHGlobal(vertexPtr);
        }
    }

    /// <summary>
    /// Creates an immutable index buffer from a list of 32-bit indices.
    /// </summary>
    internal static Buffer CreateIndexBuffer(
        Device device,
        List<int> indices)
    {
        var indexDesc = new BufferDescription
        {
            SizeInBytes = indices.Count * sizeof(int),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.IndexBuffer,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };

        var indexSize = indices.Count * sizeof(int);
        var indexPtr = Marshal.AllocHGlobal(indexSize);

        try
        {
            Marshal.Copy(indices.ToArray(), 0, indexPtr, indices.Count);

            using var indexStream = new DataStream(indexPtr, indexSize, true, true);
            return new Buffer(device, indexStream, indexDesc);
        }
        finally
        {
            Marshal.FreeHGlobal(indexPtr);
        }
    }
}
