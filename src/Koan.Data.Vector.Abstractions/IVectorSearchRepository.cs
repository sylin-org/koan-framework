using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

public interface IVectorSearchRepository<TEntity, TKey> where TEntity : IEntity<TKey> where TKey : notnull
{
    Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default) =>
        throw new NotSupportedException($"Legacy vector upsert is not supported by '{GetType().Name}'.");
    Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default) =>
        throw new NotSupportedException($"Legacy vector batch upsert is not supported by '{GetType().Name}'.");
    Task<bool> Delete(TKey id, CancellationToken ct = default) =>
        throw new NotSupportedException($"Unscoped vector delete is not supported by '{GetType().Name}'.");
    Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) =>
        throw new NotSupportedException($"Legacy vector batch delete is not supported by '{GetType().Name}'.");

    /// <summary>
    /// Retrieves the embedding vector for a specific entity by ID.
    /// Returns null if no vector exists for the given ID.
    /// </summary>
    Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default)
    {
        // Default implementation: not supported
        throw new NotSupportedException(
            $"GetEmbeddingAsync is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider implementing GetEmbeddingAsync for ID-based vector retrieval."
        );
    }

    /// <summary>
    /// Retrieves embedding vectors for multiple entities by IDs.
    /// Returns a dictionary mapping IDs to embeddings. Missing IDs are omitted.
    /// </summary>
    Task<Dictionary<TKey, float[]>> GetEmbeddings(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        // Default implementation: not supported
        throw new NotSupportedException(
            $"GetEmbeddingsAsync is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider implementing GetEmbeddingsAsync for batch vector retrieval."
        );
    }

    Task VectorEnsureCreated(CancellationToken ct = default) => Task.CompletedTask; // optional convenience
    Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default) =>
        throw new NotSupportedException($"Legacy vector query execution is not supported by '{GetType().Name}'.");

    /// <summary>Saves one complete point through the ratified Vector contract.</summary>
    Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Vector point save is not supported by '{GetType().Name}'.");

    /// <summary>Saves an ordered batch and returns one outcome per input item.</summary>
    Task<BatchResult<TKey>> Save(IReadOnlyList<VectorPoint<TKey>> points, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Vector batch save is not supported by '{GetType().Name}'.");

    /// <summary>Gets one complete point or null.</summary>
    Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Complete vector point retrieval is not supported by '{GetType().Name}'.");

    /// <summary>Deletes one point inside the compiled scope.</summary>
    Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Scoped vector delete is not supported by '{GetType().Name}'.");

    /// <summary>Gets one positional result per input identity.</summary>
    Task<IReadOnlyList<VectorPoint<TKey>?>> Get(IReadOnlyList<TKey> ids, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Positional vector retrieval is not supported by '{GetType().Name}'.");

    /// <summary>Deletes an ordered batch and returns one outcome per input item.</summary>
    Task<BatchResult<TKey>> Delete(IReadOnlyList<TKey> ids, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Vector batch delete is not supported by '{GetType().Name}'.");

    /// <summary>Runs the ratified vector query contract.</summary>
    Task<VectorSearchResult<TKey>> Search(VectorSearchRequest request, VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Current vector search is not supported by '{GetType().Name}'.");

    /// <summary>Semantically deletes every point in the current source/space.</summary>
    Task Clear(VectorScope scope, CancellationToken ct = default) =>
        throw new NotSupportedException($"Vector clear is not supported by '{GetType().Name}'.");

    /// <summary>Waits for visibility of every earlier accepted mutation.</summary>
    Task Sync(VectorScope scope, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Flush (clear) all vectors from the index. This is a destructive operation.
    /// Each adapter implements this according to its provider's capabilities.
    /// </summary>
    Task Flush(CancellationToken ct = default)
    {
        // Default implementation: throw NotSupportedException for providers without native support
        throw new NotSupportedException(
            $"Vector flush is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider implementing FlushAsync or using DeleteManyAsync for manual cleanup."
        );
    }

    /// <summary>
    /// Exports all stored vectors from the vector database in batches.
    /// Streams results to avoid materializing entire dataset in memory.
    /// Use for migration between providers, cache population, or backup operations.
    /// </summary>
    /// <param name="batchSize">Number of vectors per batch (default: provider-specific, typically 100-1000)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of vector batches with IDs, embeddings, and metadata</returns>
    IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(int? batchSize = null, CancellationToken ct = default)
    {
        // Default implementation: throw NotSupportedException for providers without native support
        throw new NotSupportedException(
            $"Vector export is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider using an adapter with native export capabilities (ElasticSearch, Weaviate, Qdrant)."
        );
    }
}
