using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using ModelViewer.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.ViewModels;

/// <summary>
/// ViewModel for the SceneModelPanel. Manages the model list UI state and
/// communicates with the rest of the app via <see cref="IMessenger"/>.
/// </summary>
public partial class SceneModelPanelViewModel : ObservableObject
{
    /// <summary>The live collection of scene models (shared with the renderer).</summary>
    public ObservableCollection<SceneModel> Models { get; }

    [ObservableProperty]
    private SceneModel? selectedModel;

    [ObservableProperty]
    private bool isExpanded = true;

    /// <summary>
    /// Initializes the ViewModel with the given model collection.
    /// </summary>
    public SceneModelPanelViewModel(ObservableCollection<SceneModel> models)
    {
        Models = models;
    }

    /// <summary>
    /// Called externally (e.g. after loading a scene) to programmatically
    /// select a model in the list.
    /// </summary>
    public void SelectModel(SceneModel? model)
    {
        SelectedModel = model;
    }

    // ── Commands ──────────────────────────────────────────────────────

    /// <summary>Opens a file dialog and sends an <see cref="AddModelRequestedMessage"/>.</summary>
    [RelayCommand]
    private void AddModel()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "3D Models|*.obj;*.fbx;*.gltf;*.glb;*.dae;*.stl|All Files|*.*",
            Title = "Add 3D Model to Scene"
        };

        if (dialog.ShowDialog() == true)
        {
            WeakReferenceMessenger.Default.Send(new AddModelRequestedMessage(dialog.FileName));
        }
    }

    /// <summary>Sends a <see cref="RemoveModelRequestedMessage"/> for the selected model.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveSelected()
    {
        if (SelectedModel is not null)
        {
            WeakReferenceMessenger.Default.Send(new RemoveModelRequestedMessage(SelectedModel));
        }
    }

    /// <summary>
    /// Shows a confirmation dialog and sends a <see cref="ClearSceneRequestedMessage"/> if confirmed.
    /// </summary>
    [RelayCommand]
    private void RemoveAll()
    {
        if (Models.Count == 0) return;

        var result = MessageBox.Show(
            $"Remove all {Models.Count} model(s) from the scene?",
            "Clear Scene",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            WeakReferenceMessenger.Default.Send(new ClearSceneRequestedMessage());
        }
    }

    private bool CanRemove() => SelectedModel is not null;

    /// <summary>Invoked when the selected model changes — broadcasts via messenger.</summary>
    partial void OnSelectedModelChanged(SceneModel? value)
    {
        WeakReferenceMessenger.Default.Send(new SceneModelSelectionChangedMessage(value));
    }

    /// <summary>Toggles the panel expanded/collapsed state.</summary>
    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }
}

