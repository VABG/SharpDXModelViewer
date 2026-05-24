using System.Windows;

namespace ModelViewer;

/// <summary>
/// Application entry point for the 3D Model Viewer.
/// </summary>
public partial class App
{
    /// <summary>
    /// Global exception handler to catch unhandled exceptions.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            MessageBox.Show($"Fatal error: {exception?.Message}", "Application Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };
        base.OnStartup(e);
    }
}