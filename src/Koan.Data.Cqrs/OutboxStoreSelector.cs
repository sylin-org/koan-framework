namespace Koan.Data.Cqrs;

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
                Priority = (f.GetType().GetCustomAttributes(typeof(Abstractions.ProviderPriorityAttribute), inherit: false).FirstOrDefault() as Abstractions.ProviderPriorityAttribute)?.Priority ?? 0,
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