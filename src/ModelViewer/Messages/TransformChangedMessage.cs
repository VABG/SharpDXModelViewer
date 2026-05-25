using CommunityToolkit.Mvvm.Messaging.Messages;
using ModelViewer.Rendering;

namespace ModelViewer.Messages;

/// <summary>
/// Message raised when a scene model's transform changes (position, rotation, scale).
/// </summary>
public sealed class TransformChangedMessage : ValueChangedMessage<SceneModel>
{
    /// <summary>The new transform after the change.</summary>
    public ModelTransform NewTransform { get; }

    public TransformChangedMessage(SceneModel model, ModelTransform newTransform)
        : base(model)
    {
        NewTransform = newTransform;
    }
}