using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelViewer.ViewModels;

/// <summary>
/// ViewModel for the StatusBar control. Exposes the status message and FPS readout
/// as bindable properties so the parent window can update them without direct
/// UI manipulation.
/// </summary>
public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "Ready";

    [ObservableProperty] private string _fpsText = "FPS: --";
}