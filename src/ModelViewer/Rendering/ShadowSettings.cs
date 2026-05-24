using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Single source of truth for all shadow and lighting parameters.
/// 
/// Implements INotifyPropertyChanged so WPF controls can data-bind directly.
/// All mutations go through property setters which raise change notifications.
/// The render thread reads a snapshot via <see cref="CaptureSnapshot"/> to avoid
/// holding locks during drawing.
/// </summary>
public class ShadowSettings : INotifyPropertyChanged
{
    // ── Defaults ──────────────────────────────────────────────────────────────
    public const float DefaultPcfRadius = 1.0f;
    public const float DefaultShadowBias = 0.002f;
    public const float DefaultShadowNormalBias = 0.008f;
    public const int DefaultShadowMapSize = 2048;

    public static readonly Vector4 DefaultLightColor = new(1.0f, 0.95f, 0.9f, 1.0f);
    public static readonly Vector4 DefaultAmbientColor = new(0.15f, 0.15f, 0.18f, 1.0f);

    // ── Thread-safe storage ───────────────────────────────────────────────────
    private readonly object _lock = new();

    private float _pcfRadius = DefaultPcfRadius;
    private float _shadowBias = DefaultShadowBias;
    private float _shadowNormalBias = DefaultShadowNormalBias;
    private Vector4 _lightColor = DefaultLightColor;
    private Vector4 _ambientColor = DefaultAmbientColor;

    // ── Shadow quality ────────────────────────────────────────────────────────

    /// <summary>
    /// PCF (Percentage-Closer Filtering) sample spread in texels.
    /// 0 = hard shadows, higher = softer edges (cost: more texture samples).
    /// </summary>
    public float PcfRadius
    {
        get => _pcfRadius;
        set => SetProperty(ref _pcfRadius, value);
    }

    /// <summary>
    /// Base depth bias to prevent shadow acne (self-shadowing artifacts).
    /// Increase if you see flickering on flat surfaces.
    /// </summary>
    public float ShadowBias
    {
        get => _shadowBias;
        set => SetProperty(ref _shadowBias, value);
    }

    /// <summary>
    /// Slope-based normal offset bias to reduce peter-panning (shadows detached from geometry).
    /// </summary>
    public float ShadowNormalBias
    {
        get => _shadowNormalBias;
        set => SetProperty(ref _shadowNormalBias, value);
    }

    // ── Lighting colors ───────────────────────────────────────────────────────

    /// <summary>
    /// Directional light color (RGBA). Default is a warm white.
    /// </summary>
    public Vector4 LightColor
    {
        get => _lightColor;
        set => SetProperty(ref _lightColor, value);
    }

    /// <summary>
    /// Ambient (fill) color (RGBA). Default is a cool dark blue-grey.
    /// </summary>
    public Vector4 AmbientColor
    {
        get => _ambientColor;
        set => SetProperty(ref _ambientColor, value);
    }

    // ── Convenience: set all shadow quality params at once ────────────────────

    /// <summary>
    /// Updates PcfRadius, ShadowBias, and ShadowNormalBias in a single call.
    /// Raises PropertyChanged for each individual property.
    /// </summary>
    public void SetShadowParams(float pcfRadius, float shadowBias, float shadowNormalBias)
    {
        PcfRadius = pcfRadius;
        ShadowBias = shadowBias;
        ShadowNormalBias = shadowNormalBias;
    }

    // ── Thread-safe snapshot for the render thread ────────────────────────────

    /// <summary>
    /// Takes a consistent snapshot of all settings under a lock.
    /// The render thread should call this once per frame instead of
    /// reading individual properties (which could yield inconsistent
    /// values if the UI thread is mutating them concurrently).
    /// </summary>
    public ShadowSettingsSnapshot CaptureSnapshot()
    {
        lock (_lock)
        {
            return new ShadowSettingsSnapshot(
                _pcfRadius,
                _shadowBias,
                _shadowNormalBias,
                _lightColor,
                _ambientColor
            );
        }
    }

    // ── Reset to defaults ─────────────────────────────────────────────────────

    /// <summary>
    /// Resets all settings to their factory defaults.
    /// </summary>
    public void Reset()
    {
        PcfRadius = DefaultPcfRadius;
        ShadowBias = DefaultShadowBias;
        ShadowNormalBias = DefaultShadowNormalBias;
        LightColor = DefaultLightColor;
        AmbientColor = DefaultAmbientColor;
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;

        // Raise on the lock so CaptureSnapshot never sees a torn state
        lock (_lock)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        return true;
    }
}

/// <summary>
/// Immutable snapshot of shadow settings, safe to read from the render thread
/// without any synchronization.
/// </summary>
public readonly struct ShadowSettingsSnapshot
{
    public float PcfRadius { get; }
    public float ShadowBias { get; }
    public float ShadowNormalBias { get; }
    public Vector4 LightColor { get; }
    public Vector4 AmbientColor { get; }

    public ShadowSettingsSnapshot(
        float pcfRadius,
        float shadowBias,
        float shadowNormalBias,
        Vector4 lightColor,
        Vector4 ambientColor)
    {
        PcfRadius = pcfRadius;
        ShadowBias = shadowBias;
        ShadowNormalBias = shadowNormalBias;
        LightColor = lightColor;
        AmbientColor = ambientColor;
    }
}