using System.Collections;
using System.Collections.Concurrent;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;

namespace Koan.Data.Vector.Connector.InMemory;

/// <summary>
/// Per-(entity, key) in-memory vector repository, routed at call time by the current EntityContext
/// partition. The store dictionary is shared with the factory; the repository looks up the right
/// bucket each call and operates on it.
/// </summary>
/// <remarks>
/// k-NN ranking runs <see cref="TensorPrimitives.CosineSimilarity(System.ReadOnlySpan{float}, System.ReadOnlySpan{float})"/>
/// (hardware-accelerated SIMD, zero native dependencies). It honestly implements every capability it can
/// model in-process — k-NN, metadata filters (the full unified <see cref="Filter"/> via
/// <see cref="DictionaryFilterEvaluator"/>, making this the convergence oracle), hybrid (vector+keyword
/// blend), offset continuation, streaming export, bulk ops, score normalization, dynamic collections. The
/// two it does NOT claim are honest omissions a single-vector dictionary cannot model: multi-vector per
/// entity and atomic batch (no transaction boundary).
/// </remarks>
internal sealed class InMemoryVectorRepository<TEntity, TKey>
    : IVectorSearchRepository<TEntity, TKey>, IDescribesCapabilities, IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly InMemoryVectorAdapterFactory _factory;
    private readonly IServiceProvider _sp;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> _stores;
    private readonly string _source;

    public InMemoryVectorRepository(
        InMemoryVectorAdapterFactory factory,
        IServiceProvider sp,
        ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> stores,
        string source = "Default")
    {
        _factory = factory;
        _sp = sp;
        _stores = stores;
        _source = source;
    }

    public void Describe(ICapabilities caps) => caps
        .Add(VectorCaps.Knn).Add(VectorCaps.Filters, FilterSupport.Full).Add(VectorCaps.Hybrid)
        .Add(VectorCaps.NativeContinuation).Add(VectorCaps.StreamingResults)
        .Add(VectorCaps.BulkUpsert).Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization).Add(VectorCaps.DynamicCollections);

    private ConcurrentDictionary<string, (float[] Embedding, object? Metadata)> Bucket()
    {
        var bucketKey = VectorAdapterNaming.GetOrCompute<TEntity>(_sp, _factory, _source);
        return _stores.GetOrAdd(bucketKey, _ => new ConcurrentDictionary<string, (float[], object?)>(StringComparer.Ordinal));
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

        // Filters: apply the metadata predicate BEFORE ranking (the convergence oracle), so this
        // adapter returns the reference id-set every real provider must match.
        var predicate = options.Filter is null
            ? null
            : DictionaryFilterEvaluator.Compile(options.Filter);

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

    public Task Flush(CancellationToken ct = default)
    {
        Bucket().Clear();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(int? batchSize = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        foreach (var kvp in Bucket())
            yield return new VectorExportBatch<TKey>(ParseKey(kvp.Key), kvp.Value.Embedding, kvp.Value.Metadata);
    }

    // The vector-index instruction surface — the in-memory reference honors the same IndexStats /
    // EnsureCreated / Clear / Rebuild instructions the search-engine connectors implement (Stats rides
    // the ExportAll capability: an enumerable store can always count its entries).
    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        switch (instruction.Name)
        {
            case VectorInstructions.IndexStats:
                return Task.FromResult((TResult)(object)Bucket().Count);
            case VectorInstructions.IndexRebuild:
                return Task.FromResult((TResult)(object)true); // no persisted index to rebuild
            case VectorInstructions.IndexClear:
                Bucket().Clear();
                return Task.FromResult(default(TResult)!);
            case VectorInstructions.IndexEnsureCreated:
                _ = Bucket();
                return Task.FromResult(default(TResult)!);
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' is not supported by the in-memory vector adapter.");
        }
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
        if (metadata is IDictionary raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (DictionaryEntry e in raw) sb.Append(e.Value).Append(' ');
            return sb.ToString();
        }
        return metadata.ToString() ?? "";
    }

    private static IReadOnlyDictionary<string, object?> ToBag(object? metadata) => metadata switch
    {
        IReadOnlyDictionary<string, object?> rod => rod,
        IDictionary<string, object?> d => new Dictionary<string, object?>(d),
        IDictionary raw => CoerceRaw(raw),
        _ => EmptyBag
    };

    private static IReadOnlyDictionary<string, object?> CoerceRaw(IDictionary raw)
    {
        var bag = new Dictionary<string, object?>();
        foreach (DictionaryEntry e in raw) bag[e.Key?.ToString() ?? ""] = e.Value;
        return bag;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyBag = new Dictionary<string, object?>();

    private static string Key(TKey id) => id?.ToString() ?? throw new ArgumentNullException(nameof(id));

    private static TKey ParseKey(string raw)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)raw;
        return (TKey)Convert.ChangeType(raw, typeof(TKey))!;
    }

    /// <summary>
    /// Cosine similarity via SIMD <see cref="TensorPrimitives"/>. Returns 0 on a dimension mismatch or a
    /// zero-norm vector (TensorPrimitives yields NaN there) — preserving the convergence-oracle contract
    /// that a non-comparable pair scores 0 rather than throwing or ranking unpredictably.
    /// </summary>
    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        var score = TensorPrimitives.CosineSimilarity<float>(a, b);
        return float.IsNaN(score) ? 0 : score;
    }
}
