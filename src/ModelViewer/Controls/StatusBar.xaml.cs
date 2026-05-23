using System.Windows.Controls;

namespace ModelViewer.Controls;

/// <summary>
/// Thin status-bar wrapper exposing <see cref="StatusText"/> and <see cref="FpsText"/>
/// as settable properties so the parent window can update them without name lookup.
/// </summary>
internal partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
    }

    /// <summary>Set the left-side status message.</summary>
    public string StatusText
    {
        set => StatusTextBlock.Text = value;
    }

    /// <summary>Set the right-side FPS readout.</summary>
    public string FpsText
    {
        set => FpsTextBlock.Text = value;
    }
}
