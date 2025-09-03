using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sora.Messaging;

namespace Sora.Flow.Sending;

internal sealed class BufferedFlowSender : IFlowSender
{
    private readonly IFlowSender _inner;
    private readonly ILogger _log;
    private readonly int _capacity;
    private readonly TimeSpan _maxItemAge;
    private readonly MessagingReadinessLifecycle _lifecycle;
    private readonly ConcurrentQueue<(object item, DateTimeOffset enqueued)> _buffer = new();
    private int _flushed;
    private int _dropped;
    private int _expired;

    public BufferedFlowSender(IFlowSender inner, ILogger log, int capacity, MessagingReadinessLifecycle lifecycle, TimeSpan? maxItemAge = null)
    {
        _inner = inner;
        _log = log;
        _capacity = capacity;
        _maxItemAge = maxItemAge ?? TimeSpan.FromMinutes(5);
        _lifecycle = lifecycle;
        _log.LogInformation("[msg] buffering enabled (capacity={Capacity}) awaiting readiness...", _capacity);
        _ = _lifecycle.Ready.ContinueWith(_ => FlushBuffered(), TaskScheduler.Default);
    }

    public bool IsReady => _lifecycle.IsReady;
    public Task Ready => _lifecycle.Ready;

    private void FlushBuffered()
    {
        var now = DateTimeOffset.UtcNow;
        var flushed = 0;
        var expired = 0;
        var dropped = 0;
        var batch = new List<FlowSendItem>(200);
        while (_buffer.TryDequeue(out var tuple))
        {
            if (now - tuple.enqueued > _maxItemAge) { expired++; continue; }
            if (tuple.item is FlowSendItem item) batch.Add(item);
            if (batch.Count >= 200)
            {
                _inner.SendAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
                flushed += batch.Count;
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            _inner.SendAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
            flushed += batch.Count;
        }
        _flushed += flushed;
        _expired += expired;
        _dropped += dropped;
        _log.LogInformation("[msg] readiness achieved; flushed {Flushed} buffered items (expired={Expired}, dropped={Dropped})", flushed, expired, dropped);
    }

    public Task SendAsync(IEnumerable<FlowSendItem> items, CancellationToken ct = default)
    {
        if (_lifecycle.IsReady)
            return _inner.SendAsync(items, ct);
        foreach (var item in items)
        {
            if (_buffer.Count >= _capacity)
            {
                _buffer.TryDequeue(out _); // drop oldest
                _dropped++;
                if (_dropped % 1000 == 0)
                    _log.LogWarning("[msg][buffer] drop-oldest engaged (dropped={Dropped})", _dropped);
            }
            _buffer.Enqueue((item, DateTimeOffset.UtcNow));
        }
        return Task.CompletedTask;
    }

    public Task SendAsync(IEnumerable<FlowSendPlainItem> items, Sora.Messaging.MessageEnvelope? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default)
    {
        // For simplicity, treat plain items as FlowSendItem for buffering
        var converted = new List<FlowSendItem>();
        foreach (var plain in items)
        {
            var evt = new FlowEvent();
            foreach (var kv in plain.Bag)
                evt.With(kv.Key, kv.Value);
            converted.Add(FlowSendItem.Of<object>(evt, plain.SourceId, plain.OccurredAt, plain.CorrelationId));
        }
        return SendAsync(converted, ct);
    }
}
