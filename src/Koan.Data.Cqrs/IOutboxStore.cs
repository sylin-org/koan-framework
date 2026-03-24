namespace Koan.Data.Cqrs;

public interface IOutboxStore
{
    Task Append(OutboxEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Dequeue up to <paramref name="max"/> pending entries for processing.
    /// Implementations should ensure entries are not delivered to multiple consumers concurrently.
    /// </summary>
    Task<IReadOnlyList<OutboxEntry>> Dequeue(int max = 100, CancellationToken ct = default);
    /// <summary>
    /// Mark an entry as processed so it won't be delivered again.
    /// </summary>
    Task MarkProcessed(string id, CancellationToken ct = default);
}