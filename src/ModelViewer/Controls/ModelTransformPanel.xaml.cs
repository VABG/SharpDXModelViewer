using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ModelViewer.Rendering;
using SharpDX;

namespace ModelViewer.Controls;

/// <summary>
/// WPF panel for editing the transform (position, rotation in degrees, scale)
/// of a selected <see cref="SceneModel"/>.
/// </summary>
internal partial class ModelTransformPanel : UserControl
{
    private SceneModel? _selectedModel;

            // ── TextBox formatting ────────────────────────────────────────
    private const string NumberFormat = "F3";

    // ── Prevent re-entrant commits during drag ────────────────────
    private bool _isCommitting;

    /// <summary>Whether the panel body is currently expanded.</summary>
    public bool IsExpanded
    {
        get => ContentGrid.Visibility == Visibility.Visible;
        set
        {
            ContentGrid.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (CollapseBtn != null)
                CollapseBtn.Tag = value ? "Expanded" : "Collapsed";
            // Rotate chevron via RenderTransform
            if (CollapseBtn != null)
            {
                var angle = value ? 0d : -90d;
                CollapseBtn.RenderTransform = new RotateTransform(angle);
                CollapseBtn.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            }
        }
    }

    public ModelTransformPanel()
    {
        InitializeComponent();
        SetEnabled(false);
        // Default: expanded
        IsExpanded = true;
    }

    /// <summary>Toggles the panel body visibility.</summary>
    private void OnCollapseToggle_Click(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    // ══════════════════════════════════════════════════════════════
    //  Public API — call from MainWindow when selection changes
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Binds the panel to a specific scene model. Pass <c>null</c> to
    /// clear the selection and disable all controls.
    /// </summary>
    public void SelectModel(SceneModel? model)
    {
        _selectedModel = model;

        if (model == null)
        {
            SelectedModelName.Text = "(no selection)";
            SetEnabled(false);
            ClearTexts();
            return;
        }

        SelectedModelName.Text = model.DisplayName;
        SetEnabled(true);
        RefreshFromModel();
    }

    // ══════════════════════════════════════════════════════════════
    //  Sync UI ← model
    // ══════════════════════════════════════════════════════════════

    private void RefreshFromModel()
    {
        if (_selectedModel == null) return;

        var t = _selectedModel.Transform;

        PosXText.Text = t.Position.X.ToString(NumberFormat, CultureInfo.InvariantCulture);
        PosYText.Text = t.Position.Y.ToString(NumberFormat, CultureInfo.InvariantCulture);
        PosZText.Text = t.Position.Z.ToString(NumberFormat, CultureInfo.InvariantCulture);

        // Rotation stored in radians → convert to degrees for display
        var rotXDeg = Math.Round(t.Rotation.X * (180.0 / Math.PI), 3);
        var rotYDeg = Math.Round(t.Rotation.Y * (180.0 / Math.PI), 3);
        var rotZDeg = Math.Round(t.Rotation.Z * (180.0 / Math.PI), 3);

        RotXText.Text = rotXDeg.ToString(NumberFormat, CultureInfo.InvariantCulture);
        RotYText.Text = rotYDeg.ToString(NumberFormat, CultureInfo.InvariantCulture);
        RotZText.Text = rotZDeg.ToString(NumberFormat, CultureInfo.InvariantCulture);

        ScaleText.Text = t.Scale.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }

            // ══════════════════════════════════════════════════════════════
    //  Sync model ← UI  (commit current text values back to the model)
    // ══════════════════════════════════════════════════════════════

    private void CommitTransform()
    {
        if (_selectedModel == null) return;

        // Prevent re-entrant commits (e.g. drag → LostFocus → Commit → Refresh → drag again)
        if (_isCommitting) return;
        _isCommitting = true;

        try
        {
            var t = _selectedModel.Transform;

            // ── Position ──────────────────────────────────────────────
            if (TryParseDouble(PosXText.Text, out var px)) t.Position.X = (float)px;
            if (TryParseDouble(PosYText.Text, out var py)) t.Position.Y = (float)py;
            if (TryParseDouble(PosZText.Text, out var pz)) t.Position.Z = (float)pz;

            // ── Rotation (degrees → radians) ──────────────────────────
            if (TryParseDouble(RotXText.Text, out var rx))
                t.Rotation.X = (float)(rx * Math.PI / 180.0);
            if (TryParseDouble(RotYText.Text, out var ry))
                t.Rotation.Y = (float)(ry * Math.PI / 180.0);
            if (TryParseDouble(RotZText.Text, out var rz))
                t.Rotation.Z = (float)(rz * Math.PI / 180.0);

            // ── Scale (guard against zero/negative) ───────────────────
            if (TryParseDouble(ScaleText.Text, out var sc) && sc > 0)
                t.Scale = (float)sc;

            _selectedModel.Transform = t;
        }
        finally
        {
            _isCommitting = false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Drag-to-adjust: real-time commit while dragging on a number field
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Raised by <see cref="DraggableNumberBehavior"/> on every drag step.
    /// Commits the new value immediately so the 3D viewport updates live.
    /// </summary>
    private void OnDragValueChanged(object? sender, RoutedEventArgs e)
    {
        // Just commit — the TextBox text is already updated by the behavior
        CommitTransform();
    }

    // ══════════════════════════════════════════════════════════════
    //  Text-box commit handlers
    // ══════════════════════════════════════════════════════════════

    private void OnPositionText_LostFocus(object? sender, RoutedEventArgs e) => CommitTransform();
    private void OnRotationText_LostFocus(object? sender, RoutedEventArgs e) => CommitTransform();
    private void OnScaleText_LostFocus(object? sender, RoutedEventArgs e) => CommitTransform();

    private void OnPositionText_KeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitTransform();
    }

    private void OnRotationText_KeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitTransform();
    }

    private void OnScaleText_KeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitTransform();
    }

        // ══════════════════════════════════════════════════════════════
    //  Place on Ground — positions the model so its lowest point sits at Z = 0
    // ══════════════════════════════════════════════════════════════

    private void OnPlaceOnGround_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedModel == null) return;

        var t = _selectedModel.Transform;

                // The bounding box is in model-space (before any transform).
        // SharpDX BoundingBox stores Center + Extents, so the lowest point is:
        //   bottomZ = BoundingBox.Center.Z - BoundingBox.Extents.Z
        // After applying the current scale, the offset from the model's origin
        // to the bottom is (Origin.Z - bottomZ) * Scale.
        // We want that bottom to sit at world Z = 0, so:
        //   newPosition.Z = offsetFromOriginToBottom
        //   = (Origin.Z - bottomZ) * Scale
        float bottomY = _selectedModel.BoundingBox.Minimum.Y * t.Scale;

        t.Position.Y = -bottomY;
        _selectedModel.Transform = t;
        RefreshFromModel();
    }

    // ══════════════════════════════════════════════════════════════
    //  Reset
    // ══════════════════════════════════════════════════════════════

    private void OnResetTransform_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedModel == null) return;
        _selectedModel.Transform = ModelTransform.Identity;
        RefreshFromModel();
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SetEnabled(bool enabled)
    {
        PosXText.IsEnabled = enabled;
        PosYText.IsEnabled = enabled;
        PosZText.IsEnabled = enabled;
        RotXText.IsEnabled = enabled;
        RotYText.IsEnabled = enabled;
        RotZText.IsEnabled = enabled;
        ScaleText.IsEnabled = enabled;
    }

    private void ClearTexts()
    {
        PosXText.Text = string.Empty;
        PosYText.Text = string.Empty;
        PosZText.Text = string.Empty;
        RotXText.Text = string.Empty;
        RotYText.Text = string.Empty;
        RotZText.Text = string.Empty;
        ScaleText.Text = string.Empty;
    }
}
