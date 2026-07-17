using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Tests.Shared;

/// <summary>
/// A deliberately <b>non-isolating</b> data adapter for the fail-closed safety-net proofs (ARCH-0099 §1 / ARCH-0101 §8 /
/// DATA-0106 §4). It is a working in-memory store that declares <c>Query.Linq</c> + <c>Query.Filter(Full)</c> and is an
/// <see cref="IQueryRepository{TEntity,TKey}"/>, but <b>does not announce <see cref="DataCaps.Isolation.RowScoped"/></b> —
/// so a managed-scoped (tenant / soft-delete / classification) entity routed to it fails closed at
/// <c>RepositoryFacade.InspectScopeAdapter</c> with the canonical "does not announce" diagnostic, and a Production boot
/// of a leaky entity onto it is refused by the data-axis pre-flight.
///
/// <para><b>Why a fake.</b> Before the ARCH-0103 fleet mandate the JSON adapter was the Docker-free "non-isolating"
/// example; once every real KV adapter realizes the Shared contract (JSON included), no real adapter is non-isolating, so
/// the safety-net tests need a fixture that is non-conformant <i>by construction</i>. It is registered explicitly by the
/// runtime fixtures (never auto-discovered) and is inert unless a source names provider <c>"fake-noniso"</c>.</para>
/// </summary>
[ProviderPriority(int.MinValue)]   // never the default/fallback election — only chosen when a source names "fake-noniso"
public sealed class NonIsolatingFakeAdapterFactory : IDataAdapterFactory
{
    public const string ProviderId = "fake-noniso";

    public string Provider => ProviderId;

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new NonIsolatingFakeRepository<TEntity, TKey>();

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };
}

/// <summary>The non-isolating repository — a plain in-memory dictionary that announces Full filtering but NOT RowScoped.</summary>
internal sealed class NonIsolatingFakeRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IDescribesCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TEntity> _store = new();

    // Announces query support but deliberately omits DataCaps.Isolation.RowScoped — the single switch that makes the
    // facade fail closed for a managed-scoped entity here.
    public void Describe(ICapabilities caps) => caps
        .Add(DataCaps.Query.Linq)
        .Add(DataCaps.Query.Filter, FilterSupport.Full);

    public Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(id, out var v) ? v : null);

    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<TEntity?>)ids.Select(id => _store.TryGetValue(id, out var v) ? v : null).ToList());

    public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        IEnumerable<TEntity> items = _store.Values;
        if (query.Filter is not null) items = items.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
        var list = items.ToList();
        return Task.FromResult(new RepositoryQueryResult<TEntity>
        {
            Items = list,
            TotalCount = list.Count,
            IsEstimate = false,
            SortHandled = RepositoryQueryResult<TEntity>.NoSortHandled,
            PaginationHandled = false,
        });
    }

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        IEnumerable<TEntity> items = _store.Values;
        if (query.Filter is not null) items = items.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
        return Task.FromResult(new CountResult(items.LongCount(), false));
    }

    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        _store[model.Id] = model;
        return Task.FromResult(model);
    }

    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var m in models) { _store[m.Id] = m; n++; }
        return Task.FromResult(n);
    }

    public Task<bool> Delete(TKey id, CancellationToken ct = default) => Task.FromResult(_store.TryRemove(id, out _));

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var id in ids) if (_store.TryRemove(id, out _)) n++;
        return Task.FromResult(n);
    }

    public Task<int> DeleteAll(CancellationToken ct = default)
    {
        var n = _store.Count;
        _store.Clear();
        return Task.FromResult(n);
    }

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        var n = _store.Count;
        _store.Clear();
        return Task.FromResult((long)n);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new Batch(this);

    private sealed class Batch(NonIsolatingFakeRepository<TEntity, TKey> repo) : IBatchSet<TEntity, TKey>
    {
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            foreach (var (id, mutate) in _mutations)
                if (repo._store.TryGetValue(id, out var cur)) { mutate(cur); _updates.Add(cur); }
            foreach (var e in _adds) repo._store[e.Id] = e;
            foreach (var e in _updates) repo._store[e.Id] = e;
            var del = 0;
            foreach (var id in _deletes) if (repo._store.TryRemove(id, out _)) del++;
            return Task.FromResult(new BatchResult(_adds.Count, _updates.Count, del));
        }
    }
}
