using Koan.Messaging.Contracts;

namespace Koan.Flow.Core.Messaging;

/// <summary>
/// Routes Flow entity messages to the dedicated "Koan.Flow.FlowEntity" queue.
/// </summary>
internal class FlowQueuedMessage : IQueuedMessage
{
    public string QueueName { get; } = "Koan.Flow.FlowEntity";
    public object Payload { get; }

    public FlowQueuedMessage(object payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}