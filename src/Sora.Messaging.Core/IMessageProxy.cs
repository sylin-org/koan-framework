using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Messaging;

/// <summary>
/// Smart proxy that routes messages to buffer (during startup) or live bus (when ready).
/// This provides the seamless experience where Send() always works from the first line of code.
/// </summary>
public interface IMessageProxy
{
    /// <summary>
    /// Sends a message. Routes to buffer if not live yet, or to message bus if live.
    /// </summary>
    Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Whether messaging is live and connected to the actual provider.
    /// </summary>
    bool IsLive { get; }
    
    /// <summary>
    /// Number of messages currently buffered (0 if live).
    /// </summary>
    int BufferedMessageCount { get; }
}

/// <summary>
/// Smart message proxy implementation with buffer → live transition.
/// </summary>
internal class AdaptiveMessageProxy : IMessageProxy
{
    private readonly IMessageBuffer _buffer;
    private volatile IMessageBus? _liveBus;
    private volatile bool _isLive;
    
    public AdaptiveMessageProxy(IMessageBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }
    
    public bool IsLive => _isLive;
    public int BufferedMessageCount => _buffer.BufferedCount;
    
    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        
        if (_isLive && _liveBus != null)
        {
            // We're live - send directly to message bus
            await _liveBus.SendAsync(message, cancellationToken);
        }
        else
        {
            // Still initializing - buffer the message
            await _buffer.BufferAsync(message, cancellationToken);
        }
    }
    
    /// <summary>
    /// Transitions from buffering to live mode.
    /// Called by the lifecycle service when messaging provider is ready.
    /// </summary>
    internal async Task GoLiveAsync(IMessageBus bus, CancellationToken cancellationToken = default)
    {
        if (_isLive)
            throw new InvalidOperationException("Already live");
        
        // Flush all buffered messages to the live bus
        var flushedCount = await _buffer.FlushToAsync(bus, cancellationToken);
        
        // Switch to live mode
        _liveBus = bus;
        _isLive = true;
        
        Console.WriteLine($"📡 Messaging is LIVE! Flushed {flushedCount} buffered messages.");
    }
    
    /// <summary>
    /// Falls back to buffering mode (e.g., if connection is lost).
    /// </summary>
    internal void FallbackToBuffering()
    {
        _isLive = false;
        _liveBus = null;
        // Note: We'd need a new buffer instance to resume buffering
        Console.WriteLine("⚠️  Messaging connection lost - would fall back to buffering");
    }
}