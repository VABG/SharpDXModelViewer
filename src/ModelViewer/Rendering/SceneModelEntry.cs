using System.Text.Json.Serialization;
using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Serializable representation of a single model instance in a scene.
/// Stores the file path and transform so it can be persisted and reloaded.
/// </summary>
public class SceneModelEntry
{
    /// <summary>Path to the source 3D model file (relative or absolute).</summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>World-space position of the model instance.</summary>
    [JsonPropertyName("position")]
    public SerializableVector3 Position { get; set; }

    /// <summary>Euler rotation in radians (X, Y, Z order).</summary>
    [JsonPropertyName("rotation")]
    public SerializableVector3 Rotation { get; set; }

    /// <summary>Uniform scale factor.</summary>
    [JsonPropertyName("scale")]
    public float Scale { get; set; } = 1.0f;

    /// <summary>
    /// Converts this entry into a <see cref="ModelTransform"/> for use in the renderer.
    /// </summary>
    public ModelTransform ToModelTransform()
    {
        return new ModelTransform(
            Position.ToVector3(),
            Rotation.ToVector3(),
            Scale
        );
    }

    /// <summary>
    /// Creates a <see cref="SceneModelEntry"/> from an existing <see cref="SceneModel"/>.
    /// </summary>
    public static SceneModelEntry FromSceneModel(SceneModel sceneModel)
    {
        var transform = sceneModel.Transform;
        return new SceneModelEntry
        {
            FilePath = sceneModel.FilePath,
            Position = new SerializableVector3(transform.Position),
            Rotation = new SerializableVector3(transform.Rotation),
            Scale = transform.Scale
        };
    }
}

/// <summary>
/// Serializable 3D vector used for JSON persistence.
/// </summary>
public struct SerializableVector3
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    public SerializableVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public SerializableVector3(Vector3 v) : this(v.X, v.Y, v.Z) { }

    public Vector3 ToVector3() => new(X, Y, Z);

    public override string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>
/// Serializable 4D vector used for color/RGBA persistence.
/// </summary>
public struct SerializableVector4
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("w")]
    public float W { get; set; }

    public SerializableVector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public SerializableVector4(Vector4 v) : this(v.X, v.Y, v.Z, v.W) { }

    public Vector4 ToVector4() => new(X, Y, Z, W);

    public override string ToString() => $"({X}, {Y}, {Z}, {W})";
}
