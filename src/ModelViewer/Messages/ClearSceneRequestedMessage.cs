using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ModelViewer.Messages;

/// <summary>
/// Message raised when the user requests to clear all models from the scene.
/// </summary>
public sealed class ClearSceneRequestedMessage : ValueChangedMessage<bool>
{
    public ClearSceneRequestedMessage()
        : base(true)
    {
    }
}