using Buffer = SharpDX.Direct3D11.Buffer;

namespace ModelViewer.Rendering;

/// <summary>
/// Abstract base for any scene object that can be drawn by the renderer.
/// Provides thread-safe access to the transform via an internal lock,
/// so the render thread can read transforms safely while the update
/// thread mutates them.
/// </summary>
public abstract class DrawableObject : IDrawableObject
{
    private readonly Lock _transformLock = new();
    private ModelTransform _transform = ModelTransform.Identity;

    /// <summary>
    /// Model-space transform for this object (position, rotation, scale).
    /// Thread-safe: reads and writes are guarded by an internal lock.
    /// </summary>
    public ModelTransform Transform
    {
        get
        {
            lock (_transformLock)
                return _transform;
        }
        set
        {
            lock (_transformLock)
                _transform = value;
        }
    }

    public abstract Buffer? VertexBuffer { get; }
    public abstract Buffer? IndexBuffer { get; }
    public abstract int IndexCount { get; }
}
