using Buffer = SharpDX.Direct3D11.Buffer;

namespace ModelViewer.Rendering;

/// <summary>
/// Common contract for any scene object that can be drawn by the renderer.
/// </summary>
public interface IDrawableObject
{
    ModelTransform Transform { get; }
    Buffer? VertexBuffer { get; }
    Buffer? IndexBuffer { get; }
    int IndexCount { get; }
}
