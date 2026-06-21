using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Koan.Messaging.Connector.InMemory;

/// <summary>
/// In-process <see cref="IMessagingProvider"/> backed by <see cref="System.Threading.Channels"/>. Always
/// available (no broker, no network), so it is the reserved Priority-10 fallback the messaging core elects
/// when no higher-priority transport (RabbitMQ, etc.) can connect. Reference = Intent: referencing this
/// package lets <c>Send()</c> / <c>On&lt;T&gt;</c> work in a single binary with zero infrastructure.
/// </summary>
public sealed class InMemoryMessagingProvider : IMessagingProvider
{
    private readonly ILogger<InMemoryMessagingProvider>? _logger;

    public InMemoryMessagingProvider(ILogger<InMemoryMessagingProvider>? logger = null) => _logger = logger;

    public string Name => "InMemory";

    // Reserved fallback slot (IMessagingProvider doc: RabbitMQ 100, AzureServiceBus 90, InMemory 10).
    public int Priority => 10;

    // In-process — there is nothing to connect to, so it is always reachable.
    public Task<bool> CanConnect(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IMessageBus> CreateBus(CancellationToken cancellationToken = default)
        => Task.FromResult<IMessageBus>(new InMemoryBus(_logger));
}

/// <summary>
/// Channels-backed in-process bus. One unbounded channel per active consumer; <see cref="SendAsync{T}"/>
/// fans a message out to every consumer subscribed to that message type. Non-durable by design — a message
/// sent with no live subscriber is dropped (durable delivery is the Jobs ledger's job, not the bus's).
/// </summary>
internal sealed class InMemoryBus : IMessageBus
{
    private readonly ConcurrentDictionary<Type, List<InMemoryConsumer>> _consumers = new();
    private readonly ILogger? _logger;

    public InMemoryBus(ILogger? logger) => _logger = logger;

    public Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        if (_consumers.TryGetValue(typeof(T), out var list))
        {
            InMemoryConsumer[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (var consumer in snapshot) consumer.Enqueue(message);
        }
        return Task.CompletedTask;
    }

    public Task<IMessageConsumer> CreateConsumerAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        var consumer = new InMemoryConsumer(typeof(T), msg => handler((T)msg), _logger, OnDisposed);
        var list = _consumers.GetOrAdd(typeof(T), _ => new List<InMemoryConsumer>());
        lock (list) list.Add(consumer);
        consumer.Start();
        return Task.FromResult<IMessageConsumer>(consumer);
    }

    public Task<bool> IsHealthy(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private void OnDisposed(InMemoryConsumer consumer)
    {
        if (_consumers.TryGetValue(consumer.MessageType, out var list))
            lock (list) list.Remove(consumer);
    }
}

/// <summary>
/// A single in-process subscription: an unbounded channel plus a pump task that invokes the handler. A
/// paused consumer leaves messages buffered in the channel and resumes draining them on <see cref="Resume"/>.
/// </summary>
internal sealed class InMemoryConsumer : IMessageConsumer
{
    private readonly Channel<object> _channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<object, Task> _handler;
    private readonly ILogger? _logger;
    private readonly Action<InMemoryConsumer> _onDisposed;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _paused;
    private volatile bool _running;
    private Task? _pump;

    public InMemoryConsumer(Type messageType, Func<object, Task> handler, ILogger? logger, Action<InMemoryConsumer> onDisposed)
    {
        MessageType = messageType;
        _handler = handler;
        _logger = logger;
        _onDisposed = onDisposed;
    }

    public Type MessageType { get; }
    public string Destination => MessageType.FullName ?? MessageType.Name;
    public bool IsActive => _running && !_paused;

    internal void Enqueue(object message) => _channel.Writer.TryWrite(message);

    internal void Start()
    {
        _running = true;
        _pump = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                // Honor pause: hold the message (already dequeued) until resumed. Pause is rare and not a
                // hot path, so a short poll keeps the gate trivially correct (no signal races).
                while (_paused && !_cts.IsCancellationRequested)
                    await Task.Delay(25, _cts.Token).ConfigureAwait(false);
                if (_cts.IsCancellationRequested) break;

                try
                {
                    await _handler(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One poison message must not kill the pump; there is no DLQ in-process.
                    _logger?.LogError(ex, "[InMemory messaging] Handler for {MessageType} threw; message dropped.", MessageType.FullName);
                }
            }
        }
        catch (OperationCanceledException) { /* disposing */ }
        finally { _running = false; }
    }

    public Task Pause() { _paused = true; return Task.CompletedTask; }

    public Task Resume() { _paused = false; return Task.CompletedTask; }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        if (_pump is not null)
        {
            try { await _pump.ConfigureAwait(false); } catch { /* already cancelled */ }
        }
        _onDisposed(this);
        _cts.Dispose();
    }
}
