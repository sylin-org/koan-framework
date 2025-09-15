namespace Koan.Data.Cqrs;

public interface IOutboxStore
{
    Task AppendAsync(OutboxEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Dequeue up to <paramref name="max"/> pending entries for processing.
    /// Implementations should ensure entries are not delivered to multiple consumers concurrently.
    /// </summary>
    Task<IReadOnlyList<OutboxEntry>> DequeueAsync(int max = 100, CancellationToken ct = default);
    /// <summary>
    /// Mark an entry as processed so it won't be delivered again.
    /// </summary>
    Task MarkProcessedAsync(string id, CancellationToken ct = default);
}