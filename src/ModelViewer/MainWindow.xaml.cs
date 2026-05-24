using System;
using System.Windows;
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
            LightPanel.Settings = _renderer.ShadowSettings;
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
}