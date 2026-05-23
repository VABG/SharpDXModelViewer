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

            // ── Wire up status bar ──────────────────────────────────────
            _renderer.OnFpsChanged = fps =>
            {
                Dispatcher.Invoke(() => StatusBar.FpsText = $"FPS: {fps}");
            };

                                    // ── Wire up scene-model panel ───────────────────────────────
                                    ScenePanel.BindModels(_renderer.ModelList.ModelsCollection);
                                    ScenePanel.ModelFileRequested += OnModelFileRequested;
                                    ScenePanel.ModelRemovalRequested += OnModelRemovalRequested;
                                    ScenePanel.ClearSceneRequested += OnClearSceneRequested;
                                    ScenePanel.SelectionChanged += OnSceneModelSelectionChanged;

            // ── Wire up light-control panel ─────────────────────────────
            LightPanel.LightDirectionChanged += _renderer.SetLightDirection;

            StatusBar.StatusText = "Ready - Open a 3D model to begin";
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

    // ────────────────────────────────────────────────────────────────────
    //  Scene-model event handlers (raised by SceneModelPanel)
    // ────────────────────────────────────────────────────────────────────

    private void OnModelFileRequested(string filePath)
    {
        try
        {
            _renderer?.AddModel(filePath);
            StatusBar.StatusText = $"Added: {System.IO.Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load model: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnModelRemovalRequested(SceneModel sceneModel)
    {
        _renderer?.RemoveModel(sceneModel);
        StatusBar.StatusText = $"Removed: {sceneModel.DisplayName}";
    }

        private void OnClearSceneRequested()
    {
        _renderer?.ModelList.Clear();
        StatusBar.StatusText = "Scene cleared";
        TransformPanel.SelectModel(null);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scene-model selection → transform panel
    // ────────────────────────────────────────────────────────────────────

    private void OnSceneModelSelectionChanged(SceneModel? selected)
    {
        TransformPanel.SelectModel(selected);
        StatusBar.StatusText = selected is null
            ? "No model selected"
            : $"Selected: {selected.DisplayName}";
    }

    // ────────────────────────────────────────────────────────────────────
    //  Menu handlers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open a file dialog to load a 3D model (legacy: replaces all models).
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
                StatusBar.StatusText = $"Loaded: {System.IO.Path.GetFileName(dialog.FileName)}";
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
}


