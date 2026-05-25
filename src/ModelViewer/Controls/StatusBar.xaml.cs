namespace ModelViewer.Controls;

/// <summary>
/// StatusBar control — uses data binding to <see cref="ViewModels.StatusBarViewModel"/>
/// and no longer exposes imperative settable properties.
/// </summary>
internal partial class StatusBar
{
    public StatusBar()
    {
        InitializeComponent();
    }
}