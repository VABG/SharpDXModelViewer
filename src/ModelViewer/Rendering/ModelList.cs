using System.Collections.ObjectModel;
using SharpDX.Direct3D11;

namespace ModelViewer.Rendering;

/// <summary>
/// Thread-safe collection that manages multiple 3D models in the scene.
/// Provides add/remove operations and exposes a read-only snapshot for the render thread.
/// </summary>
public class ModelList : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<SceneModel> _models = new();

    /// <summary>
    /// Live collection that stays in sync with the internal list.
    /// Safe for WPF data-binding on the UI thread.
    /// </summary>
    public ObservableCollection<SceneModel> ModelsCollection { get; } = [];

    /// <summary>
    /// Read-only view of the current models. Safe for the UI thread to bind to.
    /// </summary>
    public IReadOnlyList<SceneModel> Models
    {
        get
        {
            lock (_lock)
                return _models.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Number of models currently in the scene.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _models.Count;
        }
    }

    /// <summary>
    /// Event raised when models are added or removed from the list.
    /// Invoked on the calling thread (typically the UI thread via Dispatcher).
    /// </summary>
    public event Action<ModelList>? OnModelsChanged;

    /// <summary>
    /// Adds a new model to the scene.
    /// </summary>
    /// <param name="device">The D3D11 device used to create GPU buffers.</param>
    /// <param name="filePath">Path to the 3D model file.</param>
    /// <returns>The created SceneModel instance.</returns>
    public SceneModel Add(Device device, string filePath)
    {
        var model = Model.Load(device, filePath);
        var sceneModel = new SceneModel(model, filePath);

        lock (_lock)
        {
            _models.Add(sceneModel);
            ModelsCollection.Add(sceneModel);
        }

        OnModelsChanged?.Invoke(this);
        return sceneModel;
    }

    /// <summary>
    /// Removes a model from the scene and disposes its GPU resources.
    /// </summary>
    /// <param name="sceneModel">The model to remove.</param>
    /// <returns>true if the model was found and removed; false otherwise.</returns>
    public bool Remove(SceneModel sceneModel)
    {
        lock (_lock)
        {
            if (!_models.Contains(sceneModel))
                return false;

            _models.Remove(sceneModel);
            ModelsCollection.Remove(sceneModel);
        }

        sceneModel.Dispose();
        OnModelsChanged?.Invoke(this);
        return true;
    }

    /// <summary>
    /// Removes the model at the specified index and disposes its GPU resources.
    /// </summary>
    public bool RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _models.Count)
                return false;

            var sceneModel = _models[index];
            _models.RemoveAt(index);
            ModelsCollection.Remove(sceneModel);
            sceneModel.Dispose();
        }

        OnModelsChanged?.Invoke(this);
        return true;
    }

    /// <summary>
    /// Gets a snapshot of all models for safe iteration on the render thread.
    /// The returned list is a point-in-time copy and does not hold references
    /// to the internal collection.
    /// </summary>
    public SceneModel[] GetSnapshot()
    {
        lock (_lock)
            return _models.ToArray();
    }

    /// <summary>
    /// Removes all models from the scene and disposes their GPU resources.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var model in _models)
                model.Dispose();

            _models.Clear();
            ModelsCollection.Clear();
        }

        OnModelsChanged?.Invoke(this);
    }

    public void Dispose()
    {
        Clear();
    }
}