using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Sora.Data.Cqrs;

/// <summary>
/// Minimal default outbox store that logs appended entries. Useful for dev/demo until a durable store is configured.
/// </summary>
public sealed class LoggingOutboxStore : IOutboxStore
{
    private readonly ILogger<LoggingOutboxStore> _logger;
    private readonly ConcurrentQueue<OutboxEntry> _queue = new();
    private readonly ConcurrentDictionary<string, OutboxEntry> _inflight = new();
    public LoggingOutboxStore(ILogger<LoggingOutboxStore> logger) => _logger = logger;

    public Task AppendAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation("[CQRS][Outbox] {Operation} {EntityType}#{EntityId} at {At} (Id={Id})", entry.Operation, entry.EntityType, entry.EntityId, entry.OccurredAt, entry.Id);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[CQRS][Outbox] Payload: {Payload}", entry.PayloadJson);
        }
        _queue.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxEntry>> DequeueAsync(int max = 100, CancellationToken ct = default)
    {
        var list = new List<OutboxEntry>(capacity: max);
        while (list.Count < max && _queue.TryDequeue(out var entry))
        {
            _inflight[entry.Id] = entry;
            list.Add(entry);
        }
        return Task.FromResult((IReadOnlyList<OutboxEntry>)list);
    }

    public Task MarkProcessedAsync(string id, CancellationToken ct = default)
    {
        _inflight.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
