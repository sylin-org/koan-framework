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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> _stores = new(StringComparer.Ordinal);

    public string Provider => "inmemoryvector";

    public bool CanHandle(string provider)
        => string.Equals(provider, "inmemoryvector", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "inmemory", StringComparison.OrdinalIgnoreCase);

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };

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

    // AI-0036 §9: the in-memory reference implements every capability it can model in-process —
    // kNN, metadata filters (via the oracle), hybrid (vector+keyword blend), continuation paging,
    // streaming export, bulk ops, score normalization, dynamic collections. The two it does NOT
    // claim are honest omissions of features a single-vector dictionary cannot model: multi-vector
    // per entity (one embedding per id) and atomic batch (no transaction boundary).
    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn
        | VectorCapabilities.Filters
        | VectorCapabilities.Hybrid
        | VectorCapabilities.NativeContinuation
        | VectorCapabilities.StreamingResults
        | VectorCapabilities.BulkUpsert
        | VectorCapabilities.BulkDelete
        | VectorCapabilities.ScoreNormalization
        | VectorCapabilities.DynamicCollections;

    // AI-0036 §9: the in-memory adapter evaluates the full unified Filter via DictionaryFilterEvaluator
    // — it IS the convergence oracle, so it declares Full and the coordinator passes every filter through.
    public Koan.Data.Abstractions.Filtering.VectorFilterCapabilities FilterCapabilities
        => Koan.Data.Abstractions.Filtering.VectorFilterCapabilities.Full;

    private ConcurrentDictionary<string, (float[] Embedding, object? Metadata)> Bucket()
    {
        var partition = Koan.Data.Core.EntityContext.Current?.Partition;
        var storage = ((Koan.Data.Abstractions.Naming.INamingProvider)_factory).ResolveStorage(typeof(TEntity), partition, _sp);
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
        var offset = ParseOffset(options.ContinuationToken);

        // Filters: apply the metadata predicate BEFORE ranking (the convergence oracle), so the
        // in-memory adapter returns the reference id-set every real provider must match.
        var predicate = options.Filter is null
            ? null
            : Koan.Data.Abstractions.Filtering.DictionaryFilterEvaluator.Compile(options.Filter);

        // Hybrid: when SearchText + Alpha are supplied, blend the (normalized) cosine with a lexical
        // keyword score over the entry's metadata; alpha=1 => pure-vector, alpha=0 => pure-keyword.
        var hybrid = !string.IsNullOrEmpty(options.SearchText) && options.Alpha is not null;
        var alpha = options.Alpha ?? 1.0;

        var ranked = bucket
            .Where(kvp => predicate is null || predicate(ToBag(kvp.Value.Metadata)))
            .Select(kvp => (
                Id: ParseKey(kvp.Key),
                Score: hybrid
                    ? alpha * Normalize(Cosine(query, kvp.Value.Embedding)) + (1 - alpha) * Lexical(options.SearchText!, kvp.Value.Metadata)
                    : Cosine(query, kvp.Value.Embedding),
                Metadata: kvp.Value.Metadata))
            .OrderByDescending(x => x.Score)
            .ToList();

        // NativeContinuation: offset-based paging via an opaque token (the next offset).
        var page = ranked.Skip(offset).Take(topK)
            .Select(x => new VectorMatch<TKey>(x.Id, x.Score, x.Metadata))
            .ToList();
        var nextOffset = offset + topK;
        var continuation = nextOffset < ranked.Count ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        return Task.FromResult(new VectorQueryResult<TKey>(page, continuation, VectorTotalKind.Exact));
    }

    private static int ParseOffset(string? token)
        => int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var o) && o > 0 ? o : 0;

    private static double Normalize(double cosine) => (cosine + 1.0) / 2.0; // [-1,1] -> [0,1]

    // Lexical keyword score: fraction of query terms present (case-insensitive) in the stringified
    // metadata values. 0 when there is no metadata text to match — hybrid then degrades to vector.
    private static double Lexical(string text, object? metadata)
    {
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return 0;
        var hay = MetadataText(metadata);
        if (hay.Length == 0) return 0;
        var hits = terms.Count(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
        return (double)hits / terms.Length;
    }

    private static string MetadataText(object? metadata)
    {
        if (metadata is null) return "";
        if (metadata is System.Collections.IDictionary raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (System.Collections.DictionaryEntry e in raw) sb.Append(e.Value).Append(' ');
            return sb.ToString();
        }
        return metadata.ToString() ?? "";
    }

    private static IReadOnlyDictionary<string, object?> ToBag(object? metadata) => metadata switch
    {
        IReadOnlyDictionary<string, object?> rod => rod,
        IDictionary<string, object?> d => new Dictionary<string, object?>(d),
        System.Collections.IDictionary raw => CoerceRaw(raw),
        _ => EmptyBag
    };

    private static IReadOnlyDictionary<string, object?> CoerceRaw(System.Collections.IDictionary raw)
    {
        var bag = new Dictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry e in raw) bag[e.Key?.ToString() ?? ""] = e.Value;
        return bag;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyBag = new Dictionary<string, object?>();

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
