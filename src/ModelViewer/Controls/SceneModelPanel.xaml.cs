using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ModelViewer.Rendering;

namespace ModelViewer.Controls;

/// <summary>
/// Encapsulates the scene-model list with add/remove functionality.
/// Raises events so the parent window can delegate to the renderer.
/// </summary>
internal partial class SceneModelPanel : UserControl
{
    /// <summary>Fired when the user picks a file to add.</summary>
    public event Action<string>? ModelFileRequested;

    /// <summary>Fired when the user removes the selected model.</summary>
    public event Action<SceneModel>? ModelRemovalRequested;

    /// <summary>Fired when the user wants to clear the entire scene.</summary>
    public event Action? ClearSceneRequested;

    /// <summary>Fired when the selected model in the ListBox changes.</summary>
    public event Action<SceneModel?>? SelectionChanged;

    /// <summary>Set this to bind the ListBox to the live model collection.</summary>
    public ObservableCollection<SceneModel> Models { get; set; } = new();

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
                CollapseBtn.RenderTransform =
                    new System.Windows.Media.RotateTransform(angle);
                CollapseBtn.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            }
        }
    }

    public SceneModelPanel()
    {
        InitializeComponent();
        ModelListBox.ItemsSource = Models;
        ModelListBox.PreviewKeyDown += OnPreviewKeyDown;
        ModelListBox.SelectionChanged += OnModelListBoxSelectionChanged;
        // Default: collapsed
        IsExpanded = false;
    }

    /// <summary>Toggles the panel body visibility.</summary>
    private void OnCollapseToggle_Click(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>
    /// Replaces the bound collection and rebinds the ListBox so the UI
    /// reflects the new source immediately.
    /// </summary>
    public void BindModels(ObservableCollection<SceneModel> collection)
    {
        Models = collection;
        ModelListBox.ItemsSource = collection;
    }

    // ── File-dialog + Add ──────────────────────────────────────────────

    private void OnAddModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "3D Models|*.obj;*.fbx;*.gltf;*.glb;*.dae;*.stl|All Files|*.*",
            Title = "Add 3D Model to Scene"
        };

        if (dialog.ShowDialog() == true)
        {
            ModelFileRequested?.Invoke(dialog.FileName);
        }
    }

    // ── Remove selected ────────────────────────────────────────────────

    private void OnRemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ModelListBox.SelectedItem is SceneModel selected)
        {
            ModelRemovalRequested?.Invoke(selected);
        }
    }

    // ── Remove all ─────────────────────────────────────────────────────

    private void OnRemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (Models.Count == 0) return;

        var result = MessageBox.Show(
            $"Remove all {Models.Count} model(s) from the scene?",
            "Clear Scene",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            ClearSceneRequested?.Invoke();
    }

    // ── Keyboard shortcut: Delete key ──────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            OnRemoveSelected_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── ListBox selection change → notify parent ───────────────────────

    private void OnModelListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SelectionChanged?.Invoke(ModelListBox.SelectedItem as SceneModel);
    }
}

