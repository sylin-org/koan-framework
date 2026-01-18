using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Tests.Data.Core.Support;

/// <summary>
/// In-memory fake vector repository for testing.
/// Allows deterministic error injection and state inspection.
/// </summary>
public sealed class FakeVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, VectorEntry> _vectors = new();
    private readonly List<(TKey Id, float[] Embedding, object? Metadata)> _operations = new();
    private readonly object _lock = new();

    public bool ThrowOnUpsert { get; set; }
    public bool ThrowOnDelete { get; set; }
    public bool ThrowOnSearch { get; set; }
    public Exception? CustomException { get; set; }

    public int? RequiredEmbeddingDimension { get; set; } = 1536;

    public IReadOnlyList<(TKey Id, float[] Embedding, object? Metadata)> Operations
    {
        get
        {
            lock (_lock)
            {
                return _operations.ToList();
            }
        }
    }

    public int VectorCount => _vectors.Count;

    public bool ContainsVector(TKey id) => _vectors.ContainsKey(id);

    public float[]? GetVector(TKey id) => _vectors.TryGetValue(id, out var entry) ? entry.Embedding : null;

    public void Clear()
    {
        _vectors.Clear();
        lock (_lock)
        {
            _operations.Clear();
        }
    }

    public Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        if (ThrowOnUpsert)
        {
            throw CustomException ?? new InvalidOperationException("FakeVectorRepository configured to fail on upsert");
        }

        if (RequiredEmbeddingDimension.HasValue && embedding.Length != RequiredEmbeddingDimension.Value)
        {
            throw new ArgumentException(
                $"Invalid embedding dimension. Expected {RequiredEmbeddingDimension.Value}, got {embedding.Length}",
                nameof(embedding));
        }

        _vectors[id] = new VectorEntry(embedding, metadata);

        lock (_lock)
        {
            _operations.Add((id, embedding, metadata));
        }

        return Task.CompletedTask;
    }

    public Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        if (ThrowOnUpsert)
        {
            throw CustomException ?? new InvalidOperationException("FakeVectorRepository configured to fail on upsert");
        }

        var count = 0;
        foreach (var (id, embedding, metadata) in items)
        {
            if (RequiredEmbeddingDimension.HasValue && embedding.Length != RequiredEmbeddingDimension.Value)
            {
                throw new ArgumentException(
                    $"Invalid embedding dimension. Expected {RequiredEmbeddingDimension.Value}, got {embedding.Length}",
                    nameof(embedding));
            }

            _vectors[id] = new VectorEntry(embedding, metadata);
            count++;
        }

        return Task.FromResult(count);
    }

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        if (ThrowOnDelete)
        {
            throw CustomException ?? new InvalidOperationException("FakeVectorRepository configured to fail on delete");
        }

        return Task.FromResult(_vectors.TryRemove(id, out _));
    }

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        if (ThrowOnDelete)
        {
            throw CustomException ?? new InvalidOperationException("FakeVectorRepository configured to fail on delete");
        }

        var count = 0;
        foreach (var id in ids)
        {
            if (_vectors.TryRemove(id, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<float[]?> GetEmbeddingAsync(TKey id, CancellationToken ct = default)
    {
        return Task.FromResult(_vectors.TryGetValue(id, out var entry) ? entry.Embedding : null);
    }

    public Task<Dictionary<TKey, float[]>> GetEmbeddingsAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<TKey, float[]>();
        foreach (var id in ids)
        {
            if (_vectors.TryGetValue(id, out var entry))
            {
                result[id] = entry.Embedding;
            }
        }

        return Task.FromResult(result);
    }

    public Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
    {
        if (ThrowOnSearch)
        {
            throw CustomException ?? new InvalidOperationException("FakeVectorRepository configured to fail on search");
        }

        // Simple cosine similarity search
        var query = options.Query;
        var topK = options.TopK ?? 10;

        var results = _vectors
            .Select(kvp => new
            {
                Id = kvp.Key,
                Similarity = CosineSimilarity(query, kvp.Value.Embedding)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .Select(x => new VectorMatch<TKey>(x.Id, x.Similarity, null))
            .ToList();

        return Task.FromResult(new VectorQueryResult<TKey>(
            results,
            ContinuationToken: null,
            TotalKind: VectorTotalKind.Exact
        ));
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        _vectors.Clear();
        lock (_lock)
        {
            _operations.Clear();
        }
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    private sealed record VectorEntry(float[] Embedding, object? Metadata);
}
