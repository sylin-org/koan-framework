using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

/// <summary>
/// Production-shaped in-memory <see cref="IVectorAdapterFactory"/> used by the InMemory cell of
/// the vector matrix. The factory creates per-(entity, partition) <see cref="InMemoryVectorRepository{TEntity, TKey}"/>
/// stores keyed by the adapter's own <see cref="INamingProvider.ResolveStorage"/> output — which
/// gives the matrix a real per-partition isolation surface without any external infrastructure.
/// </summary>
public sealed class InMemoryVectorAdapterFactory : IVectorAdapterFactory
{
    private readonly ConcurrentDictionary<(Type, string?), string> _nameCache = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> _stores = new(StringComparer.Ordinal);

    public string Provider => "inmemoryvector";

    public bool CanHandle(string provider)
        => string.Equals(provider, "inmemoryvector", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "inmemory", StringComparison.OrdinalIgnoreCase);

    public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
    {
        var trimmed = partition?.Trim();
        var key = (entityType, string.IsNullOrEmpty(trimmed) ? null : trimmed);
        return _nameCache.GetOrAdd(key, _ =>
            string.IsNullOrEmpty(trimmed) ? entityType.Name : entityType.Name + "#" + trimmed);
    }

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new InMemoryVectorRepository<TEntity, TKey>(this, sp, _stores);

    /// <summary>Test-only: wipe every store the factory has ever issued.</summary>
    public void ClearAll() => _stores.Clear();
}

/// <summary>
/// Per-(entity, key) repository routed at call time by the current EntityContext partition. The
/// store dictionary is shared with the factory; the repository's job is to look up the right
/// bucket each time and operate on it.
/// </summary>
internal sealed class InMemoryVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>, IVectorCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly InMemoryVectorAdapterFactory _factory;
    private readonly IServiceProvider _sp;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> _stores;

    public InMemoryVectorRepository(
        InMemoryVectorAdapterFactory factory,
        IServiceProvider sp,
        ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> stores)
    {
        _factory = factory;
        _sp = sp;
        _stores = stores;
    }

    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn
        | VectorCapabilities.BulkUpsert
        | VectorCapabilities.BulkDelete
        | VectorCapabilities.ScoreNormalization
        | VectorCapabilities.DynamicCollections;

    private ConcurrentDictionary<string, (float[] Embedding, object? Metadata)> Bucket()
    {
        var partition = Koan.Data.Core.EntityContext.Current?.Partition;
        var storage = _factory.ResolveStorage(typeof(TEntity), partition, _sp);
        return _stores.GetOrAdd(storage, _ => new ConcurrentDictionary<string, (float[], object?)>(StringComparer.Ordinal));
    }

    public Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        Bucket()[Key(id)] = (embedding, metadata);
        return Task.CompletedTask;
    }

    public Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        var bucket = Bucket();
        var count = 0;
        foreach (var (id, embedding, metadata) in items)
        {
            bucket[Key(id)] = (embedding, metadata);
            count++;
        }
        return Task.FromResult(count);
    }

    public Task<bool> Delete(TKey id, CancellationToken ct = default)
        => Task.FromResult(Bucket().TryRemove(Key(id), out _));

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var bucket = Bucket();
        var count = 0;
        foreach (var id in ids)
            if (bucket.TryRemove(Key(id), out _)) count++;
        return Task.FromResult(count);
    }

    public Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default)
        => Task.FromResult(Bucket().TryGetValue(Key(id), out var entry) ? entry.Embedding : null);

    public Task<Dictionary<TKey, float[]>> GetEmbeddings(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var bucket = Bucket();
        var result = new Dictionary<TKey, float[]>();
        foreach (var id in ids)
            if (bucket.TryGetValue(Key(id), out var entry))
                result[id] = entry.Embedding;
        return Task.FromResult(result);
    }

    public Task VectorEnsureCreated(CancellationToken ct = default)
    {
        _ = Bucket(); // materialize the bucket eagerly
        return Task.CompletedTask;
    }

    public Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        var bucket = Bucket();
        var query = options.Query;
        var topK = options.TopK ?? 10;

        var ranked = bucket
            .Select(kvp => (Id: ParseKey(kvp.Key), Similarity: Cosine(query, kvp.Value.Embedding), Metadata: kvp.Value.Metadata))
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .Select(x => new VectorMatch<TKey>(x.Id, x.Similarity, x.Metadata))
            .ToList();

        return Task.FromResult(new VectorQueryResult<TKey>(ranked, null, VectorTotalKind.Exact));
    }

    public Task Flush(CancellationToken ct = default)
    {
        Bucket().Clear();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(int? batchSize = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        foreach (var kvp in Bucket())
            yield return new VectorExportBatch<TKey>(ParseKey(kvp.Key), kvp.Value.Embedding, kvp.Value.Metadata);
    }

    private static string Key(TKey id) => id?.ToString() ?? throw new ArgumentNullException(nameof(id));

    private static TKey ParseKey(string raw)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)raw;
        return (TKey)Convert.ChangeType(raw, typeof(TKey))!;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom == 0 ? 0 : dot / denom;
    }
}
