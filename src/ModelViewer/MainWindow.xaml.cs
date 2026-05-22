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
        // Update the display text for the changed slider
        if (sender == LightXSlider)
            LightXText.Text = e.NewValue.ToString("F2");
        else if (sender == LightYSlider)
            LightYText.Text = e.NewValue.ToString("F2");
        else if (sender == LightZSlider)
            LightZText.Text = e.NewValue.ToString("F2");

        // Guard: during XAML initialization, not all sliders are constructed yet.
        // Only update the renderer once all three sliders exist.
        if (LightXSlider == null || LightYSlider == null || LightZSlider == null)
            return;

        // Build the raw direction vector from slider values
        var direction = new Vector3(
            (float)LightXSlider.Value,
            (float)LightYSlider.Value,
            (float)LightZSlider.Value);

        // Send to renderer (it will normalize internally)
        _renderer?.SetLightDirection(direction);
    }

    /// <summary>
    /// Normalize the light direction vector and update slider values.
    /// </summary>
    private void NormalizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var direction = new Vector3(
            (float)LightXSlider.Value,
            (float)LightYSlider.Value,
            (float)LightZSlider.Value);

        var length = direction.Length();
        if (length < 0.001f)
        {
            MessageBox.Show("Light direction vector is too small to normalize.",
                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        direction = Vector3.Normalize(direction);

        // Update sliders
        LightXSlider.Value = direction.X;
        LightYSlider.Value = direction.Y;
        LightZSlider.Value = direction.Z;

        // Update display text
        LightXText.Text = direction.X.ToString("F2");
        LightYText.Text = direction.Y.ToString("F2");
        LightZText.Text = direction.Z.ToString("F2");

        // Update renderer
        _renderer?.SetLightDirection(direction);
    }
}

