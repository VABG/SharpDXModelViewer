using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SharpDX;

namespace ModelViewer.Controls;

/// <summary>
/// Encapsulates the light-direction sliders and direction-preview readout.
/// Raises <see cref="LightDirectionChanged"/> whenever the direction vector changes.
/// </summary>
internal partial class LightControlPanel : UserControl
{
    /// <summary>
    /// Fired whenever yaw or pitch changes, carrying the resulting 3D direction.
    /// </summary>
    public event Action<Vector3>? LightDirectionChanged;

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

    public LightControlPanel()
    {
        InitializeComponent();
        // Default: expanded
        IsExpanded = true;
    }

    /// <summary>Toggles the panel body visibility.</summary>
    private void OnCollapseToggle_Click(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    private void OnSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: during XAML initialization not all controls exist yet
        if (YawSlider == null || PitchSlider == null || DirectionPreview == null)
            return;

        // Update the display text for the changed slider
        if (sender == YawSlider)
            YawText.Text = $"{e.NewValue:F0}°";
        else if (sender == PitchSlider)
            PitchText.Text = $"{e.NewValue:F0}°";

        // Convert yaw / pitch to a 3D direction vector
        float yawRad = (float)(YawSlider.Value * Math.PI / 180.0);
        float pitchRad = (float)((PitchSlider.Value - 90.0) * Math.PI / 180.0);

        float x = (float)(Math.Cos(pitchRad) * Math.Cos(yawRad));
        float y = (float)Math.Sin(pitchRad);
        float z = (float)(Math.Cos(pitchRad) * Math.Sin(yawRad));

        var direction = new Vector3(x, y, z);
        direction.Normalize();

        // Update direction preview text
        DirectionPreview.Text = $"X: {x:F2}, Y: {y:F2}, Z: {z:F2}";

        // Notify parent
        LightDirectionChanged?.Invoke(direction);
    }
}
