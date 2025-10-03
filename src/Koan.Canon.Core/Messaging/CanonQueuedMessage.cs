using Koan.Messaging.Contracts;

namespace Koan.Canon.Core.Messaging;

/// <summary>
/// Routes Canon entity messages to the dedicated "Koan.Canon.CanonEntity" queue.
/// </summary>
internal class CanonQueuedMessage : IQueuedMessage
{
    public string QueueName { get; } = "Koan.Canon.CanonEntity";
    public object Payload { get; }

    public CanonQueuedMessage(object payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}


