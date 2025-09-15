using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Messaging;

/// <summary>
/// Buffers messages during startup until the messaging provider is live.
/// Ensures no messages are lost during the initialization phase.
/// </summary>
public interface IMessageBuffer
{
    /// <summary>
    /// Stores a message in the buffer to be sent later when the provider is ready.
    /// </summary>
    Task BufferAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Flushes all buffered messages to the live message bus.
    /// Returns the number of messages successfully flushed.
    /// </summary>
    Task<int> FlushToAsync(IMessageBus bus, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Number of messages currently buffered.
    /// </summary>
    int BufferedCount { get; }
    
    /// <summary>
    /// Whether the buffer is still accepting messages (i.e., provider not live yet).
    /// </summary>
    bool IsBuffering { get; }
}

/// <summary>
/// In-memory message buffer for startup buffering.
/// </summary>
internal class InMemoryMessageBuffer : IMessageBuffer
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<BufferedMessage> _messages = new();
    private volatile bool _isBuffering = true;
    
    public int BufferedCount => _messages.Count;
    public bool IsBuffering => _isBuffering;
    
    public Task BufferAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        if (!_isBuffering)
            throw new InvalidOperationException("Buffer is no longer accepting messages - provider should be live");
        
        _messages.Enqueue(new BufferedMessage(
            MessageType: typeof(T),
            Payload: message,
            Timestamp: DateTimeOffset.UtcNow));
        
        return Task.CompletedTask;
    }
    
    public async Task<int> FlushToAsync(IMessageBus bus, CancellationToken cancellationToken = default)
    {
        var flushed = 0;
        
        // Stop accepting new buffered messages
        _isBuffering = false;
        
        // Send all buffered messages
        while (_messages.TryDequeue(out var bufferedMessage))
        {
            try
            {
                // Use reflection to call SendAsync<T> with the correct type
                var sendMethod = typeof(IMessageBus)
                    .GetMethod(nameof(IMessageBus.SendAsync))!
                    .MakeGenericMethod(bufferedMessage.MessageType);
                
                await (Task)sendMethod.Invoke(bus, new[] { bufferedMessage.Payload, cancellationToken })!;
                flushed++;
            }
            catch (Exception ex)
            {
                // Log error but continue flushing other messages
                System.Diagnostics.Debug.WriteLine($"Failed to flush buffered message: {ex.Message}");
            }
        }
        
        return flushed;
    }
}

/// <summary>
/// Represents a message that was buffered during startup.
/// </summary>
internal record BufferedMessage(
    Type MessageType,
    object Payload,
    DateTimeOffset Timestamp);