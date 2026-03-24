namespace Koan.Data.Cqrs;

/// <summary>
/// In-memory outbox store for development and tests. Single-process only.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<OutboxEntry> _queue = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OutboxEntry> _inflight = new();

    public Task Append(OutboxEntry entry, CancellationToken ct = default)
    { _queue.Enqueue(entry); return Task.CompletedTask; }

    public Task<IReadOnlyList<OutboxEntry>> Dequeue(int max = 100, CancellationToken ct = default)
    {
        var list = new List<OutboxEntry>(capacity: max);
        while (list.Count < max && _queue.TryDequeue(out var entry))
        { _inflight[entry.Id] = entry; list.Add(entry); }
        return Task.FromResult((IReadOnlyList<OutboxEntry>)list);
    }

    public Task MarkProcessed(string id, CancellationToken ct = default)
    { _inflight.TryRemove(id, out _); return Task.CompletedTask; }
}