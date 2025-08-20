using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Cqrs;

/// <summary>
/// Outbox entry used by implicit CQRS; generic envelope to avoid user code.
/// </summary>
public sealed record OutboxEntry(
    string Id,
    DateTimeOffset OccurredAt,
    string EntityType,
    string Operation, // Upsert/Delete
    string EntityId,
    string PayloadJson,
    string? CorrelationId = null,
    string? CausationId = null
);

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

/// <summary>
/// Factory for creating outbox stores; discovered via DI with ProviderPriority.
/// </summary>
public interface IOutboxStoreFactory
{
    string Provider { get; }
    IOutboxStore Create(IServiceProvider sp);
}

public sealed class InMemoryOutboxOptions
{
    // Reserved for parity across providers (e.g., capacity or partitioning later)
}

/// <summary>
/// In-memory outbox store for development and tests. Single-process only.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<OutboxEntry> _queue = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OutboxEntry> _inflight = new();

    public Task AppendAsync(OutboxEntry entry, CancellationToken ct = default)
    { _queue.Enqueue(entry); return Task.CompletedTask; }

    public Task<IReadOnlyList<OutboxEntry>> DequeueAsync(int max = 100, CancellationToken ct = default)
    {
        var list = new List<OutboxEntry>(capacity: max);
        while (list.Count < max && _queue.TryDequeue(out var entry))
        { _inflight[entry.Id] = entry; list.Add(entry); }
        return Task.FromResult((IReadOnlyList<OutboxEntry>)list);
    }

    public Task MarkProcessedAsync(string id, CancellationToken ct = default)
    { _inflight.TryRemove(id, out _); return Task.CompletedTask; }
}

[Sora.Data.Abstractions.ProviderPriority(0)]
public sealed class InMemoryOutboxFactory : IOutboxStoreFactory
{
    public string Provider => "inmemory";
    public IOutboxStore Create(IServiceProvider sp) => new InMemoryOutboxStore();
}

/// <summary>
/// Default outbox store provider that selects a concrete implementation based on registered factories and priority.
/// This allows switching to Mongo by merely referencing the package.
/// </summary>
internal sealed class OutboxStoreSelector : IOutboxStore
{
    private readonly IOutboxStore _inner;
    public OutboxStoreSelector(IServiceProvider sp, IEnumerable<IOutboxStoreFactory> factories)
    {
        var list = factories.ToList();
        if (list.Count == 0)
        {
            _inner = new InMemoryOutboxStore();
            return;
        }
        var ranked = list
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(Sora.Data.Abstractions.ProviderPriorityAttribute), inherit: false).FirstOrDefault() as Sora.Data.Abstractions.ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _inner = ranked.First().Factory.Create(sp);
    }

    public Task AppendAsync(OutboxEntry entry, CancellationToken ct = default) => _inner.AppendAsync(entry, ct);
    public Task<IReadOnlyList<OutboxEntry>> DequeueAsync(int max = 100, CancellationToken ct = default) => _inner.DequeueAsync(max, ct);
    public Task MarkProcessedAsync(string id, CancellationToken ct = default) => _inner.MarkProcessedAsync(id, ct);
}

public static class InMemoryOutboxRegistration
{
    public static IServiceCollection AddInMemoryOutbox(this IServiceCollection services)
    {
        services.BindOutboxOptions<InMemoryOutboxOptions>("InMemory");
        services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
        return services;
    }
}
