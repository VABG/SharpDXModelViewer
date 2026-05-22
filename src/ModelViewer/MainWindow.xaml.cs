using System;
using System.Windows;
using Microsoft.Win32;
using ModelViewer.Rendering;
using SharpDX;

namespace ModelViewer;

/// <summary>
/// Main window for the 3D Model Viewer application.
/// Integrates WPF with SharpDX via HwndHost and manages the render loop.
/// </summary>
public partial class MainWindow : Window
{
    private Renderer? _renderer;
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// Initialize the SharpDX rendering surface when the window loads.
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Create and attach the D3D rendering surface
            var surface = new D3DRenderSurface();
            RenderHost.Content = surface;

            // Wait for the surface to be ready, then initialize renderer
            await surface.ReadyAsync();
            _renderer = new Renderer(surface);

            // Wire up FPS reporting from the render thread to the UI thread
            _renderer.OnFpsChanged = fps =>
            {
                Dispatcher.Invoke(() => FpsText.Text = $"FPS: {fps}");
            };

            StatusText.Text = "Ready - Open a 3D model to begin";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize renderer: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>
    /// Cleanup renderer when window is closing.
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderer?.Dispose();
    }

    /// <summary>
    /// Open a file dialog to load a 3D model.
    /// </summary>
    private void OpenModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "3D Models|*.obj;*.fbx;*.gltf;*.glb;*.dae;*.stl|All Files|*.*",
            Title = "Open 3D Model"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _renderer?.LoadModel(dialog.FileName);
                StatusText.Text = $"Loaded: {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load model: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Reset the camera to its default position.
    /// </summary>
    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        _renderer?.ResetCamera();
    }

    /// <summary>
    /// Exit the application.
    /// </summary>
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Slider value changed - update the light direction in the renderer.
    /// </summary>
    private void LightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: during XAML initialization, not all controls are constructed yet.
        // Skip entirely until the full XAML tree is loaded.
        if (YawSlider == null || PitchSlider == null || DirectionPreview == null)
            return;

        // Update the display text for the changed slider
        if (sender == YawSlider)
            YawText.Text = $"{e.NewValue:F0}°";
        else if (sender == PitchSlider)
            PitchText.Text = $"{e.NewValue:F0}°";

        // Convert yaw/pitch to a 3D direction vector
        // Yaw: rotation around Y axis (-180 to 180)
        // Pitch: 0° = straight down, 90° = horizon, 180° = straight up
        float yawRad = (float)(YawSlider.Value * Math.PI / 180.0);
        float pitchRad = (float)((PitchSlider.Value - 90.0) * Math.PI / 180.0);

        float x = (float)(Math.Cos(pitchRad) * Math.Cos(yawRad));
        float y = (float)Math.Sin(pitchRad);
        float z = (float)(Math.Cos(pitchRad) * Math.Sin(yawRad));

        var direction = new Vector3(x, y, z);
        direction.Normalize();

        // Update direction preview text
        DirectionPreview.Text = $"X: {x:F2}, Y: {y:F2}, Z: {z:F2}";

        // Send to renderer
        _renderer?.SetLightDirection(direction);
    }

}


