namespace Sora.Messaging.Contracts;

/// <summary>
/// Represents a message with specific queue routing requirements.
/// When an interceptor returns an IQueuedMessage, the messaging system
/// will route it to the specified queue instead of using default routing.
/// </summary>
public interface IQueuedMessage
{
    /// <summary>
    /// The target queue name for this message.
    /// </summary>
    string QueueName { get; }
    
    /// <summary>
    /// The actual message payload to send.
    /// </summary>
    object Payload { get; }
}