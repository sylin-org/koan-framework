using Sora.Messaging.Contracts;

namespace Sora.Flow.Core.Messaging;

/// <summary>
/// Routes Flow entity messages to the dedicated "Sora.Flow.FlowEntity" queue.
/// </summary>
internal class FlowQueuedMessage : IQueuedMessage
{
    public string QueueName { get; } = "Sora.Flow.FlowEntity";
    public object Payload { get; }

    public FlowQueuedMessage(object payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}