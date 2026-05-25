using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ModelViewer.Messages;

/// <summary>
/// Message raised when the user requests to add a model file to the scene.
/// </summary>
public sealed class AddModelRequestedMessage : ValueChangedMessage<string>
{
    public AddModelRequestedMessage(string filePath)
        : base(filePath)
    {
    }
}