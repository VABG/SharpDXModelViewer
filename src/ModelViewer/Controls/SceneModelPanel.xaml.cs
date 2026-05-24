using System.Windows.Controls;

namespace ModelViewer.Controls;

/// <summary>
/// Encapsulates the scene-model list with add/remove functionality.
/// Communicates with the rest of the app via <c>CommunityToolkit.Mvvm.Messaging</c>.
/// </summary>
internal partial class SceneModelPanel : UserControl
{
    public SceneModelPanel()
    {
        InitializeComponent();
    }
}
