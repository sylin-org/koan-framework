using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public interface IVectorSearchRepository<TEntity, TKey> where TEntity : IEntity<TKey> where TKey : notnull
{
    Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default);
    Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default);

    Task VectorEnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask; // optional convenience
    Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default);

    /// <summary>
    /// Exports all stored vectors from the vector database in batches.
    /// Streams results to avoid materializing entire dataset in memory.
    /// Use for migration between providers, cache population, or backup operations.
    /// </summary>
    /// <param name="batchSize">Number of vectors per batch (default: provider-specific, typically 100-1000)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of vector batches with IDs, embeddings, and metadata</returns>
    IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(int? batchSize = null, CancellationToken ct = default)
    {
        // Default implementation: throw NotSupportedException for providers without native support
        throw new NotSupportedException(
            $"Vector export is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider using an adapter with native export capabilities (ElasticSearch, Weaviate, Qdrant)."
        );
    }
}