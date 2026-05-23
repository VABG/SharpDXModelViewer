using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Assimp;
using SharpDX;
using SharpDX.Direct3D11;

namespace ModelViewer.Rendering;

/// <summary>
/// Represents a 3D model loaded via AssimpNet, with vertex/index buffers and material data.
/// </summary>
public class Model : DrawableObject
{
    private SharpDX.Direct3D11.Buffer? _vertexBuffer;
    private SharpDX.Direct3D11.Buffer? _indexBuffer;
    private int _indexCount;
    private bool _disposed;

    public override int IndexCount => _indexCount;
    public override SharpDX.Direct3D11.Buffer? VertexBuffer => _vertexBuffer;
    public override SharpDX.Direct3D11.Buffer? IndexBuffer => _indexBuffer;

    /// <summary>
    /// Loads a 3D model file and creates D3D11 vertex/index buffers.
    /// Supports OBJ, FBX, GLTF, GLB, DAE, and STL formats.
    /// </summary>
    public static Model Load(SharpDX.Direct3D11.Device device, string filePath)
    {
        // AssimpNet 4.1.0 uses AssimpContext
        using var importer = new AssimpContext();
        var scene = importer.ImportFile(filePath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateNormals |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.SortByPrimitiveType |
            PostProcessSteps.FlipUVs |
            PostProcessSteps.FlipWindingOrder);

        if (scene == null || scene.Meshes.Count == 0)
            throw new InvalidOperationException("No meshes found in the model file.");

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();

        foreach (var mesh in scene.Meshes)
        {
            foreach (var face in mesh.Faces)
            {
                for (int i = 0; i < face.IndexCount; i++)
                {
                    var vertexIndex = face.Indices[i];
                    var vertex = mesh.Vertices[vertexIndex];

                    var normal = mesh.HasNormals && vertexIndex < mesh.Normals.Count
                        ? mesh.Normals[vertexIndex]
                        : new Vector3D(0, 1, 0);

                    var uv = mesh.TextureCoordinateChannelCount > 0
                             && vertexIndex < mesh.TextureCoordinateChannels[0].Count
                        ? new Vector2D(mesh.TextureCoordinateChannels[0][vertexIndex].X, mesh.TextureCoordinateChannels[0][vertexIndex].Y)
                        : new Vector2D(0, 0);

                    vertices.Add(new VertexPositionNormalTexture(
                        new Vector3(vertex.X, vertex.Y, vertex.Z),
                        new Vector3(normal.X, normal.Y, normal.Z),
                        new Vector2(uv.X, uv.Y)
                    ));

                    // Use the actual vertex list index as the draw index
                    indices.Add(vertices.Count - 1);
                }
            }
        }

        var model = new Model();
        model.CreateBuffers(device, vertices, indices);
        return model;
    }

        private void CreateBuffers(Device device, List<VertexPositionNormalTexture> vertices, List<int> indices)
    {
        _indexCount = indices.Count;
        _vertexBuffer = BufferHelpers.CreateVertexBuffer(device, vertices);
        _indexBuffer = BufferHelpers.CreateIndexBuffer(device, indices);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Vertex layout matching the HLSL vertex shader input.
/// Contains position, normal, and texture coordinate data.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VertexPositionNormalTexture
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TextureCoordinate;

    public const int SizeInBytes = 12 + 12 + 8; // 32 bytes per vertex

    public VertexPositionNormalTexture(Vector3 position, Vector3 normal, Vector2 textureCoordinate)
    {
        Position = position;
        Normal = normal;
        TextureCoordinate = textureCoordinate;
    }
}

