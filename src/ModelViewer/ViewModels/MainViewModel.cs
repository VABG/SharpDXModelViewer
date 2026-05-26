using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using ModelViewer.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.ViewModels;

/// <summary>
/// ViewModel for MainWindow. Manages the renderer lifecycle, messenger handlers,
/// and menu commands (save/load scene, reset camera, exit).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // ── Renderer (owned by the ViewModel) ─────────────────────────────

    private Renderer? _renderer;

    public Renderer? Renderer => _renderer;

    // ── Child ViewModels ─────────────────────────────────────────────

    public StatusBarViewModel StatusBarVm { get; }

    [ObservableProperty] private SceneModelPanelViewModel? _scenePanelVm;

    public ModelTransformViewModel TransformPanelVm { get; }

    // ── Construction ─────────────────────────────────────────────────

    public MainViewModel()
    {
        StatusBarVm = new StatusBarViewModel();
        TransformPanelVm = new ModelTransformViewModel();
        // ScenePanelVm is created lazily in InitializeRenderer() because it
        // needs the model collection from the Renderer.

        RegisterMessengerHandlers();
    }

    // ── Renderer lifecycle ───────────────────────────────────────────

    /// <summary>
    /// Called after the D3DRenderSurface is ready. Initializes the Renderer
    /// and wires up all event callbacks.
    /// </summary>
    public void InitializeRenderer(D3DRenderSurface surface)
    {
        _renderer = new Renderer(surface);

        // FPS updates flow through the StatusBar ViewModel
        _renderer.OnFpsChanged = fps => { StatusBarVm.FpsText = $"FPS: {fps}"; };

        // Now that the renderer exists, create the ScenePanel ViewModel
        ScenePanelVm = new SceneModelPanelViewModel(_renderer.ModelList.ModelsCollection);
    }

    /// <summary>
    /// Returns the DirectionalLightSettings from the renderer (for LightPanel wiring).
    /// </summary>
    public DirectionalLightSettings? LightSettings => _renderer?.DirectionalLightSettings;

    // ── Messenger registration & handlers ────────────────────────────

    private void RegisterMessengerHandlers()
    {
        WeakReferenceMessenger.Default.Register<AddModelRequestedMessage>(this, (recipient, message) =>
        {
            try
            {
                var sceneModel = _renderer?.AddModel(message.Value);
                StatusBarVm.StatusText = $"Added: {Path.GetFileName(message.Value)}";
                ScenePanelVm?.SelectModel(sceneModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load model: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });

        WeakReferenceMessenger.Default.Register<RemoveModelRequestedMessage>(this, (recipient, message) =>
        {
            _renderer?.RemoveModel(message.Value);
            StatusBarVm.StatusText = $"Removed: {message.Value.DisplayName}";
        });

        WeakReferenceMessenger.Default.Register<ClearSceneRequestedMessage>(this, (recipient, message) =>
        {
            _renderer?.ModelList.Clear();
            StatusBarVm.StatusText = "Scene cleared";
            TransformPanelVm.SelectModel(null);
        });

        WeakReferenceMessenger.Default.Register<SceneModelSelectionChangedMessage>(this, (recipient, message) =>
        {
            TransformPanelVm.SelectModel(message.Value);
            StatusBarVm.StatusText = message.Value is null
                ? "No model selected"
                : $"Selected: {message.Value.DisplayName}";
        });
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void ResetCamera()
    {
        _renderer?.ResetCamera();
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }

    // ── Scene save / load ────────────────────────────────────────────

    [RelayCommand]
    private void SaveScene()
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

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            scene.SaveToFile(dialog.FileName);
            StatusBarVm.StatusText = $"Scene saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save scene: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void LoadScene()
    {
        if (_renderer == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Load Scene",
            Filter = "Scene files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var scene = Scene.LoadFromFile(dialog.FileName);
            scene.ApplyToRenderer(_renderer);

            StatusBarVm.StatusText = $"Scene loaded: {Path.GetFileName(dialog.FileName)}";

            // Re-select the first model if any exist
            var models = _renderer.ModelList.GetSnapshot();
            if (models.Length > 0)
            {
                ScenePanelVm?.SelectModel(models[0]);
            }
            else
            {
                TransformPanelVm.SelectModel(null);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load scene: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Cleanup ──────────────────────────────────────────────────────

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _renderer?.Dispose();
    }
}