using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace ModelViewer.Rendering;

/// <summary>
/// Represents a single 3D model instance in the scene with metadata for the UI.
/// Wraps a <see cref="Model"/> and exposes a transform that can be manipulated
/// independently per-instance.  Implements <see cref="IDrawableObject"/> so the
/// renderer can read this transform directly.
/// </summary>
public class SceneModel : IDrawableObject, IDisposable
{
    private readonly Model _model;
    private readonly object _transformLock = new();
    private ModelTransform _transform = ModelTransform.Identity;

    /// <summary>Display name shown in the model list UI.</summary>
    public string DisplayName { get; }

    /// <summary>Original file path the model was loaded from.</summary>
    public string FilePath { get; }

    /// <summary>The underlying D3D model resource.</summary>
    public Model Model => _model;

    /// <summary>AABB bounding box of the model in its original (model-space) coordinates.</summary>
    public BoundingBox BoundingBox => _model.BoundingBox;

    // ── IDrawableObject — delegates buffer access to inner Model ──────

    /// <inheritdoc />
    public Buffer? VertexBuffer => _model.VertexBuffer;

    /// <inheritdoc />
    public Buffer? IndexBuffer => _model.IndexBuffer;

    /// <inheritdoc />
    public int IndexCount => _model.IndexCount;

    /// <summary>
    /// Model-space transform for this instance. Thread-safe: reads and writes
    /// are guarded by an internal lock so the render thread can safely read
    /// while the UI/update threads mutate.
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

    public SceneModel(Model model, string filePath)
    {
        _model = model;
        FilePath = filePath;
        DisplayName = System.IO.Path.GetFileNameWithoutExtension(filePath);
    }

    public void Dispose()
    {
        _model.Dispose();
    }
}

