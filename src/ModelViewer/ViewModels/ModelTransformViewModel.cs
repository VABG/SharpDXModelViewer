using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ModelViewer.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.ViewModels;

/// <summary>
/// ViewModel for <see cref="Controls.ModelTransformPanel"/>. Exposes position, rotation
/// (in degrees), and scale as editable properties. Commits changes back to the
/// underlying <see cref="SceneModel"/> directly and broadcasts via messenger.
/// </summary>
public partial class ModelTransformViewModel : ObservableObject
{
    private SceneModel? _model;

    // ── Display state ────────────────────────────────────────────────

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasModel))]
    private string _modelName = "(no selection)";

    [ObservableProperty] private bool _hasModel;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChevronAngle))]
    private bool _isExpanded = true;

    /// <summary>Chevron rotation angle: 0 when expanded, -90 when collapsed.</summary>
    public double ChevronAngle => IsExpanded ? 0d : -90d;

    // ── Position ─────────────────────────────────────────────────────

    [ObservableProperty] private double _posX;

    [ObservableProperty] private double _posY;

    [ObservableProperty] private double _posZ;

    // ── Rotation (degrees) ───────────────────────────────────────────

    [ObservableProperty] private double _rotX;

    [ObservableProperty] private double _rotY;

    [ObservableProperty] private double _rotZ;

    // ── Scale ────────────────────────────────────────────────────────

    [ObservableProperty] private double _scale = 1.0;

    // ── Construction ─────────────────────────────────────────────────

    public ModelTransformViewModel()
    {
    }

    /// <summary>
    /// Binds this ViewModel to a scene model and populates the properties.
    /// Pass <c>null</c> to clear the selection.
    /// </summary>
    public void SelectModel(SceneModel? model)
    {
        _model = model;

        if (model == null)
        {
            ModelName = "(no selection)";
            HasModel = false;
            PosX = 0;
            PosY = 0;
            PosZ = 0;
            RotX = 0;
            RotY = 0;
            RotZ = 0;
            Scale = 1.0;
            return;
        }

        ModelName = model.DisplayName;
        HasModel = true;
        RefreshFromModel();
    }

    // ══════════════════════════════════════════════════════════════
    //  Sync UI ← model
    // ══════════════════════════════════════════════════════════════

    private void RefreshFromModel()
    {
        if (_model == null) return;

        var t = _model.Transform;

        PosX = t.Position.X;
        PosY = t.Position.Y;
        PosZ = t.Position.Z;

        // Rotation: radians → degrees
        RotX = Math.Round(t.Rotation.X * (180.0 / Math.PI), 3);
        RotY = Math.Round(t.Rotation.Y * (180.0 / Math.PI), 3);
        RotZ = Math.Round(t.Rotation.Z * (180.0 / Math.PI), 3);

        Scale = t.Scale;
    }

    // ══════════════════════════════════════════════════════════════
    //  Sync model ← UI  (commit via messenger)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Commits the current property values back to the scene model and sends
    /// a <see cref="TransformChangedMessage"/>.
    /// </summary>
    private void CommitTransform()
    {
        if (_model == null) return;

        var t = _model.Transform;

        t.Position.X = (float)PosX;
        t.Position.Y = (float)PosY;
        t.Position.Z = (float)PosZ;

        // Rotation: degrees → radians
        t.Rotation.X = (float)(RotX * Math.PI / 180.0);
        t.Rotation.Y = (float)(RotY * Math.PI / 180.0);
        t.Rotation.Z = (float)(RotZ * Math.PI / 180.0);

        // Guard against zero/negative scale
        if (Scale > 0)
            t.Scale = (float)Scale;

        _model.Transform = t;

        // Notify subscribers (e.g., renderer needs to update constant buffers)
        WeakReferenceMessenger.Default.Send(new TransformChangedMessage(_model, t));
    }

    // ── Auto-commit on property change ───────────────────────────────

    partial void OnPosXChanged(double value) => CommitTransform();
    partial void OnPosYChanged(double value) => CommitTransform();
    partial void OnPosZChanged(double value) => CommitTransform();

    partial void OnRotXChanged(double value) => CommitTransform();
    partial void OnRotYChanged(double value) => CommitTransform();
    partial void OnRotZChanged(double value) => CommitTransform();

    partial void OnScaleChanged(double value)
    {
        // Clamp to positive
        if (value <= 0)
        {
            Scale = 0.01;
            return;
        }

        CommitTransform();
    }

    // ══════════════════════════════════════════════════════════════
    //  Commands
    // ══════════════════════════════════════════════════════════════

    /// <summary>Toggles the panel expanded/collapsed state.</summary>
    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>Places the model so its lowest point sits at Y = 0.</summary>
    [RelayCommand]
    private void PlaceOnGround()
    {
        if (_model == null) return;

        var t = _model.Transform;
        float bottomY = _model.BoundingBox.Minimum.Y * t.Scale;
        t.Position.Y = -bottomY;
        _model.Transform = t;

        RefreshFromModel();
    }

    /// <summary>Resets the transform to identity.</summary>
    [RelayCommand]
    private void ResetTransform()
    {
        if (_model == null) return;

        _model.Transform = ModelTransform.Identity;
        RefreshFromModel();
    }
}