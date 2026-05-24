using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Serializable container for a complete scene.
/// Holds model instances (file paths + transforms), lighting settings,
/// and camera state so the entire scene can be persisted and restored.
/// </summary>
public class Scene
{
    /// <summary>Human-readable name for this scene.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Scene";

    /// <summary>Collection of model instances in the scene.</summary>
    [JsonPropertyName("models")]
    public List<SceneModelEntry> Models { get; set; } = new();

    /// <summary>Directional light configuration.</summary>
    [JsonPropertyName("lightSettings")]
    public SceneLightSettings LightSettings { get; set; } = new();

    /// <summary>Camera position/orientation when the scene was saved.</summary>
    [JsonPropertyName("camera")]
    public SceneCameraState Camera { get; set; } = new();

    /// <summary>
    /// Serializes this scene to a JSON string.
    /// </summary>
    public string Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Deserializes a scene from a JSON string.
    /// </summary>
    public static Scene Deserialize(string json)
    {
        return JsonSerializer.Deserialize<Scene>(json) ?? new Scene();
    }

    /// <summary>
    /// Saves this scene to a file.
    /// </summary>
    public void SaveToFile(string filePath)
    {
        string json = Serialize();
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a scene from a file.
    /// </summary>
    public static Scene LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);
        return Deserialize(json);
    }

    /// <summary>
    /// Populates this scene from the current renderer state.
    /// </summary>
    public void CaptureFromRenderer(Renderer renderer)
    {
        // Capture models
        Models.Clear();
        foreach (var sm in renderer.ModelList.GetSnapshot())
        {
            Models.Add(SceneModelEntry.FromSceneModel(sm));
        }

        // Capture light settings
        LightSettings.CaptureFromSettings(renderer.DirectionalLightSettings);

        // Capture camera state
        Camera.CaptureFromCamera(renderer.Camera);
    }

    /// <summary>
    /// Applies this scene to a renderer (loads models, restores settings).
    /// </summary>
    public void ApplyToRenderer(Renderer renderer)
    {
        // Clear existing models
        renderer.ModelList.Clear();

        // Load each model from file
        foreach (var entry in Models)
        {
            if (File.Exists(entry.FilePath))
            {
                var sceneModel = renderer.AddModel(entry.FilePath);
                sceneModel.Transform = entry.ToModelTransform();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Scene: Could not load model '{entry.FilePath}' - file not found.");
            }
        }

        // Apply light settings
        renderer.DirectionalLightSettings.PcfRadius = LightSettings.PcfRadius;
        renderer.DirectionalLightSettings.ShadowBias = LightSettings.ShadowBias;
        renderer.DirectionalLightSettings.ShadowNormalBias = LightSettings.ShadowNormalBias;
        renderer.DirectionalLightSettings.LightColor = LightSettings.LightColor.ToVector4();
        renderer.DirectionalLightSettings.AmbientColor = LightSettings.AmbientColor.ToVector4();

        // Apply camera state
        Camera.ApplyToCamera(renderer.Camera);
    }
}

/// <summary>
/// Serializable snapshot of directional light settings.
/// </summary>
public class SceneLightSettings
{
    [JsonPropertyName("pcfRadius")]
    public float PcfRadius { get; set; } = DirectionalLightSettings.DefaultPcfRadius;

    [JsonPropertyName("shadowBias")]
    public float ShadowBias { get; set; } = DirectionalLightSettings.DefaultShadowBias;

    [JsonPropertyName("shadowNormalBias")]
    public float ShadowNormalBias { get; set; } = DirectionalLightSettings.DefaultShadowNormalBias;

    [JsonPropertyName("lightColor")]
    public SerializableVector4 LightColor { get; set; } =
        new SerializableVector4(DirectionalLightSettings.DefaultLightColor);

    [JsonPropertyName("ambientColor")]
    public SerializableVector4 AmbientColor { get; set; } =
        new SerializableVector4(DirectionalLightSettings.DefaultAmbientColor);

    public void CaptureFromSettings(DirectionalLightSettings settings)
    {
        PcfRadius = settings.PcfRadius;
        ShadowBias = settings.ShadowBias;
        ShadowNormalBias = settings.ShadowNormalBias;
        LightColor = new SerializableVector4(settings.LightColor);
        AmbientColor = new SerializableVector4(settings.AmbientColor);
    }
}

/// <summary>
/// Serializable snapshot of camera state.
/// </summary>
public class SceneCameraState
{
    [JsonPropertyName("distance")]
    public float Distance { get; set; } = 50.0f;

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; } = MathF.PI / 6;

    [JsonPropertyName("yaw")]
    public float Yaw { get; set; } = MathF.PI / 4;

    [JsonPropertyName("target")]
    public SerializableVector3 Target { get; set; }

    public SceneCameraState()
    {
        Target = new SerializableVector3(Vector3.Zero);
    }

    /// <summary>
    /// Reads the current camera state.
    /// Uses reflection to access private fields since Camera doesn't expose setters.
    /// </summary>
    public void CaptureFromCamera(Camera camera)
    {
        // Access private fields via reflection
        var type = typeof(Camera);

        var distanceField = type.GetField("_distance",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (distanceField != null) Distance = (float)distanceField.GetValue(camera)!;

        var pitchField = type.GetField("_pitch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pitchField != null) Pitch = (float)pitchField.GetValue(camera)!;

        var yawField = type.GetField("_yaw",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (yawField != null) Yaw = (float)yawField.GetValue(camera)!;

        var targetField = type.GetField("_target",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (targetField != null) Target = new SerializableVector3((Vector3)targetField.GetValue(camera)!);
    }

    /// <summary>
    /// Restores camera state by writing to private fields via reflection.
    /// </summary>
    public void ApplyToCamera(Camera camera)
    {
        var type = typeof(Camera);

        var distanceField = type.GetField("_distance",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (distanceField != null) distanceField.SetValue(camera, Distance);

        var pitchField = type.GetField("_pitch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pitchField != null) pitchField.SetValue(camera, Pitch);

        var yawField = type.GetField("_yaw",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (yawField != null) yawField.SetValue(camera, Yaw);

        var targetField = type.GetField("_target",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (targetField != null) targetField.SetValue(camera, Target.ToVector3());

        // Force the view matrix to recalculate
        var updateMethod = type.GetMethod("UpdateViewMatrix",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        updateMethod?.Invoke(camera, null);
    }
}
