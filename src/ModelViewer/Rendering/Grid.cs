using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace ModelViewer.Rendering;

/// <summary>
/// Renders a reference grid on the XZ plane so the camera orientation
/// is visible even when no model is loaded.
/// </summary>
public class Grid : IDisposable
{
    private Buffer? _vertexBuffer;
    private Buffer? _indexBuffer;
    private int _indexCount;
    private bool _disposed;

    /// <summary>
    /// Model-space transform for this grid (position, rotation, scale).
    /// Mutate this from the render thread before the next frame renders.
    /// </summary>
    public ModelTransform Transform { get; set; } = ModelTransform.Identity;

    /// <summary>
    /// Number of divisions along each axis (grid will have divisionsCount + 1 lines per axis).
    /// </summary>
    public int DivisionsCount { get; }

    /// <summary>
    /// Total width/height of the grid in world units.
    /// </summary>
    private float Size { get; }

    public int IndexCount => _indexCount;
    public Buffer? VertexBuffer => _vertexBuffer;
    public Buffer? IndexBuffer => _indexBuffer;

    /// <summary>
    /// Creates a grid centered at the origin on the XZ plane.
    /// </summary>
    /// <param name="device">The D3D11 device to create buffers on.</param>
    /// <param name="size">Total width/height of the grid in world units.</param>
    /// <param name="divisionsCount">Number of subdivisions per axis.</param>
    public static Grid Create(Device device, float size = 200.0f, int divisionsCount = 20)
    {
        var grid = new Grid(size, divisionsCount);
        grid.CreateBuffers(device);
        return grid;
    }

    private Grid(float size, int divisionsCount)
    {
        Size = size;
        DivisionsCount = divisionsCount;
    }

    private void CreateBuffers(Device device)
    {
        var (vertices, indices) = GenerateGridMesh();

        // ── Vertex buffer ──────────────────────────────────────────────────────
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
                Marshal.StructureToPtr(vertices[i], IntPtr.Add(vertexPtr, i * VertexPositionNormalTexture.SizeInBytes), false);
            }
            using var vertexStream = new DataStream(vertexPtr, vertexSize, true, true);
            _vertexBuffer = new Buffer(device, vertexStream, vertexDesc);
        }
        finally
        {
            Marshal.FreeHGlobal(vertexPtr);
        }

        // ── Index buffer ───────────────────────────────────────────────────────
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
            _indexBuffer = new Buffer(device, indexStream, indexDesc);
        }
        finally
        {
            Marshal.FreeHGlobal(indexPtr);
        }

        _indexCount = indices.Count;
    }

    /// <summary>
    /// Generates a grid mesh made of thin rectangles along the X and Z axes.
    /// The grid lies flat on the XZ plane at Y = 0.
    /// </summary>
    private (List<VertexPositionNormalTexture> Vertices, List<int> Indices) GenerateGridMesh()
    {
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();

        float halfSize = Size * 0.5f;
        float step = Size / DivisionsCount;

        // Slight Y offset so grid quads don't z-fight with models resting on Y=0
        const float gridY = -0.01f;

        // Normal always points up
        var upNormal = new Vector3(0.0f, 1.0f, 0.0f);

        // Thickness of each grid line
        float lineThickness = step * 0.02f;

        // ── Generate grid lines as thin rectangles ─────────────────────────────
        // We draw lines along X and Z axes. Each "line" is a thin rectangle
        // made of 2 triangles (6 indices).

        for (int i = 0; i <= DivisionsCount; i++)
        {
            float t = -halfSize + i * step;

            // ── Line along X axis at position Z = t ─────────────────────────
            {
                int baseVertex = vertices.Count;

                // Rectangle vertices (clockwise winding for back-face culling)
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(-halfSize, gridY, t - lineThickness), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(halfSize, gridY, t - lineThickness), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(halfSize, gridY, t + lineThickness), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(-halfSize, gridY, t + lineThickness), upNormal, new Vector2(0, 0)));

                indices.Add(baseVertex);
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 3);
            }

            // ── Line along Z axis at position X = t ─────────────────────────
            {
                int baseVertex = vertices.Count;

                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(t - lineThickness, gridY, -halfSize), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(t + lineThickness, gridY, -halfSize), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(t + lineThickness, gridY, halfSize), upNormal, new Vector2(0, 0)));
                vertices.Add(new VertexPositionNormalTexture(
                    new Vector3(t - lineThickness, gridY, halfSize), upNormal, new Vector2(0, 0)));

                indices.Add(baseVertex);
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 3);
            }
        }

        return (vertices, indices);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _disposed = true;
    }
}
