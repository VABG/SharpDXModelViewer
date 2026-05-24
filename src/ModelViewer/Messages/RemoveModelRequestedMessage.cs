using CommunityToolkit.Mvvm.Messaging.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.Messages;

/// <summary>
/// Message raised when the user requests to remove a specific model from the scene.
/// </summary>
public sealed class RemoveModelRequestedMessage : ValueChangedMessage<SceneModel>
{
    public RemoveModelRequestedMessage(SceneModel model)
        : base(model)
    {
    }
}
