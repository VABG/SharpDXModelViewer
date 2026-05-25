using System.Windows;
using ModelViewer.ViewModels;

namespace ModelViewer;

/// <summary>
/// Main window for the 3D Model Viewer application.
/// Thin code-behind — business logic lives in <see cref="MainViewModel"/>.
/// Only responsibilities here: D3D surface init and window lifecycle.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel _mainVm;

    public MainWindow()
    {
        InitializeComponent();

        _mainVm = new MainViewModel();
        DataContext = _mainVm;

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
            var surface = new Rendering.D3DRenderSurface();
            RenderHost.Content = surface;

            // Wait for the surface to be ready, then initialize renderer
            await surface.ReadyAsync();
            _mainVm.InitializeRenderer(surface);

            // ── Wire up light-control panel (uses events, not MVVM yet) ──
            var lightSettings = _mainVm.LightSettings;
            if (lightSettings is not null)
            {
                LightPanel.Settings = lightSettings;
                LightPanel.LightDirectionChanged += _mainVm.Renderer!.SetLightDirection;
                LightPanel.ShadowParamsChanged += _mainVm.Renderer!.SetShadowParams;
                LightPanel.LightColorsChanged += _mainVm.Renderer!.SetLightColors;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize renderer: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>
    /// Cleanup ViewModel and messenger when window is closing.
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _mainVm.Dispose();
    }
}