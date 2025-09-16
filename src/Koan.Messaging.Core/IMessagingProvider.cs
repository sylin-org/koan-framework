using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Messaging;

/// <summary>
/// Contract for messaging providers (RabbitMQ, Azure Service Bus, etc).
/// Providers self-register via IKoanAutoRegistrar and are auto-selected based on availability.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>
    /// Provider name for identification (e.g., "RabbitMQ", "AzureServiceBus", "InMemory").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Priority for auto-selection. Higher values are preferred.
    /// RabbitMQ: 100, AzureServiceBus: 90, InMemory: 10
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Checks if this provider can establish a connection in the current environment.
    /// Used during auto-selection to find the first working provider.
    /// </summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates and initializes a message bus instance.
    /// Called only after CanConnectAsync returns true.
    /// </summary>
    Task<IMessageBus> CreateBusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The actual message bus that sends and receives messages.
/// Provider-specific implementations hide transport details.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Sends a message to the appropriate destination based on its type.
    /// </summary>
    Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Creates a consumer for messages of the specified type.
    /// </summary>
    Task<IMessageConsumer> CreateConsumerAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Checks if the bus connection is healthy.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an active message consumer.
/// </summary>
public interface IMessageConsumer : IAsyncDisposable
{
    /// <summary>
    /// The message type this consumer handles.
    /// </summary>
    Type MessageType { get; }
    
    /// <summary>
    /// Queue or topic name this consumer listens to.
    /// </summary>
    string Destination { get; }
    
    /// <summary>
    /// Whether the consumer is actively processing messages.
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// Pauses message consumption.
    /// </summary>
    Task PauseAsync();
    
    /// <summary>
    /// Resumes message consumption.
    /// </summary>
    Task ResumeAsync();
}