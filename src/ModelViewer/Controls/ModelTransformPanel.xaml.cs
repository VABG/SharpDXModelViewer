using System.Windows.Controls;
namespace ModelViewer.Controls;

/// <summary>
/// WPF panel for editing the transform (position, rotation in degrees, scale)
/// of a selected scene model. Uses <see cref="ViewModels.ModelTransformViewModel"/>
/// for MVVM-based data binding.
/// </summary>
internal partial class ModelTransformPanel : UserControl
{
    public ModelTransformPanel()
    {
        InitializeComponent();
    }
}

