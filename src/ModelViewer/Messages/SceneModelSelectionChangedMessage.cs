using CommunityToolkit.Mvvm.Messaging.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.Messages;

/// <summary>
/// Message raised when the selected model in the scene model list changes.
/// </summary>
public sealed class SceneModelSelectionChangedMessage : ValueChangedMessage<SceneModel?>
{
    public SceneModelSelectionChangedMessage(SceneModel? model)
        : base(model)
    {
    }
}
