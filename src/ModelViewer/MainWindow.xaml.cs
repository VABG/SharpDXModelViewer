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
            _renderer.OnFpsChanged = fps => { Dispatcher.Invoke(() => StatusBar.FpsText = $"FPS: {fps}"); };

            // ── Wire up scene-model panel ───────────────────────────────
            ScenePanel.BindModels(_renderer.ModelList.ModelsCollection);
            ScenePanel.ModelFileRequested += OnModelFileRequested;
            ScenePanel.ModelRemovalRequested += OnModelRemovalRequested;
            ScenePanel.ClearSceneRequested += OnClearSceneRequested;
            ScenePanel.SelectionChanged += OnSceneModelSelectionChanged;

            // ── Wire up light-control panel ─────────────────────────────
            LightPanel.Settings = _renderer.DirectionalLightSettings;
            LightPanel.LightDirectionChanged += _renderer.SetLightDirection;
            LightPanel.ShadowParamsChanged += _renderer.SetShadowParams;
            LightPanel.LightColorsChanged += _renderer.SetLightColors;

            StatusBar.StatusText = "Ready";
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
            var sceneModel = _renderer?.AddModel(filePath);
            StatusBar.StatusText = $"Added: {System.IO.Path.GetFileName(filePath)}";

            // Auto-select the newly added model in the SceneModels view
            ScenePanel.SelectModel(sceneModel);
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

    // ────────────────────────────────────────────────────────────────────
    //  Scene save / load
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the current scene (models, transforms, light settings, camera) to a JSON file.
    /// </summary>
    private void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        if (_renderer == null) return;

        var scene = new Scene();
        scene.CaptureFromRenderer(_renderer);

        var dialog = new SaveFileDialog
        {
            Title = "Save Scene",
            Filter = "Scene files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = scene.Name + ".json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            scene.SaveToFile(dialog.FileName);
            StatusBar.StatusText = $"Scene saved: {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save scene: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Loads a scene from a JSON file, replacing the current scene.
    /// </summary>
    private void LoadScene_Click(object sender, RoutedEventArgs e)
    {
        if (_renderer == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Load Scene",
            Filter = "Scene files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var scene = Scene.LoadFromFile(dialog.FileName);
            scene.ApplyToRenderer(_renderer);

            StatusBar.StatusText = $"Scene loaded: {System.IO.Path.GetFileName(dialog.FileName)}";

            // Re-select the first model if any exist
            var models = _renderer.ModelList.GetSnapshot();
            if (models.Count > 0)
            {
                ScenePanel.SelectModel(models[0]);
            }
            else
            {
                TransformPanel.SelectModel(null);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load scene: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}