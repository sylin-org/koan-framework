using Newtonsoft.Json;
using Koan.Data.Abstractions;

namespace Koan.Data.Cqrs;

/// <summary>
/// Implicit CQRS decorator: records generic events to outbox and optionally mirrors 1:1 to a read source.
/// </summary>
internal sealed class CqrsRepositoryDecorator<TEntity, TKey> : IDataRepository<TEntity, TKey>, IQueryCapabilities, IWriteCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IOutboxStore? _outbox;
    private readonly ICqrsRouting _routing;

    public CqrsRepositoryDecorator(IDataRepository<TEntity, TKey> inner, IServiceProvider sp, Microsoft.Extensions.Options.IOptions<CqrsOptions> options)
    {
        _inner = inner;
        _routing = (ICqrsRouting?)sp.GetService(typeof(ICqrsRouting)) ?? throw new InvalidOperationException("CQRS routing not available.");
        _outbox = sp.GetService(typeof(IOutboxStore)) as IOutboxStore;
    }

    public QueryCapabilities Capabilities => (_inner as IQueryCapabilities)?.Capabilities ?? QueryCapabilities.None;
    public WriteCapabilities Writes => (_inner as IWriteCapabilities)?.Writes ?? WriteCapabilities.None;

    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        var repo = _routing.GetReadRepository<TEntity, TKey>();
        return repo.GetAsync(id, ct);
    }
    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        var repo = _routing.GetReadRepository<TEntity, TKey>();
        return repo.QueryAsync(query, ct);
    }

    public Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
    {
        var repo = _routing.GetReadRepository<TEntity, TKey>();
        return repo.CountAsync(request, ct);
    }
    public Task<IReadOnlyList<TEntity>> QueryAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    => (_routing.GetReadRepository<TEntity, TKey>() as ILinqQueryRepository<TEntity, TKey>)?.QueryAsync(predicate, ct) ?? Task.FromResult<IReadOnlyList<TEntity>>(Array.Empty<TEntity>());

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _routing.GetWriteRepository<TEntity, TKey>().UpsertAsync(model, ct);
        await RecordOutbox("Upsert", result, ct);
        return result;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        var ok = await _routing.GetWriteRepository<TEntity, TKey>().DeleteAsync(id, ct);
        if (ok) await RecordOutbox("Delete", default, ct, id);
        return ok;
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = await _routing.GetWriteRepository<TEntity, TKey>().UpsertManyAsync(models, ct);
        await RecordOutboxMany("Upsert", models, ct);
        return count;
    }

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    => _routing.GetWriteRepository<TEntity, TKey>().DeleteManyAsync(ids, ct);

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        var n = await _routing.GetWriteRepository<TEntity, TKey>().DeleteAllAsync(ct);
        // Optional: outbox could record a summary event; we’ll skip event flood for delete-all.
        return n;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => _routing.GetWriteRepository<TEntity, TKey>().CreateBatch();

    private async Task RecordOutbox(string op, TEntity? model, CancellationToken ct, TKey? id = default)
    {
        if (_outbox is null) return;
        var entityId = id is not null ? id!.ToString()! : model is not null ? model.Id?.ToString() ?? string.Empty : string.Empty;
    var payload = model is not null ? JsonConvert.SerializeObject(model) : "{}";
        var entry = new OutboxEntry(Guid.CreateVersion7().ToString("n"), DateTimeOffset.UtcNow, typeof(TEntity).AssemblyQualifiedName!, op, entityId, payload);
        await _outbox.AppendAsync(entry, ct);
    }

    private async Task RecordOutboxMany(string op, IEnumerable<TEntity> models, CancellationToken ct)
    {
        if (_outbox is null) return;
        foreach (var m in models)
            await RecordOutbox(op, m, ct);
    }

    // Mirroring is handled asynchronously by the OutboxProcessor based on the active profile.
}
