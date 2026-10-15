using System.Windows;
using System.IO;

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
        const String diagPath = @"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\renderlog.txt";

        try { System.IO.File.AppendAllText(diagPath, "App.OnStartup\n"); } catch { }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            try
            {
                System.IO.File.AppendAllText(diagPath,
                    $"UNHANDLED {exception?.GetType()}: {exception?.Message}\n{exception?.StackTrace}\n");
            }
            catch { }
            MessageBox.Show($"Fatal error: {exception?.Message}", "Application Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };
        base.OnStartup(e);
    }
}