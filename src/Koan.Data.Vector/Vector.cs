using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Vector;

// SoC-friendly vector facade: lives in Vector assembly; no changes to core Entity<>
public class Vector<TEntity> where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
    => (((Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IVectorService))) as IVectorService)?.TryGetRepository<TEntity, string>())
            ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    private static IVectorSearchRepository<TEntity, string>? TryRepo
    => ((Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IVectorService))) as IVectorService)?.TryGetRepository<TEntity, string>();

    public static bool IsAvailable => TryRepo is not null;

    /// <summary>
    /// Scopes all Vector&lt;TEntity&gt; operations to the specified partition.
    /// Convenience method that delegates to EntityContext.Partition().
    /// Consistent with Data&lt;T&gt;.WithPartition() pattern (DATA-0077).
    /// </summary>
    /// <param name="partition">Partition identifier (e.g., "project-abc", "tenant-123")</param>
    /// <returns>Disposable scope - use with 'using' statement</returns>
    /// <example>
    /// <code>
    /// using (Vector&lt;Document&gt;.WithPartition("project-koan"))
    /// {
    ///     await Vector&lt;Document&gt;.Save(items);  // Scoped to project-koan partition
    /// }
    /// </code>
    /// </example>
    public static IDisposable WithPartition(string partition)
        => Koan.Data.Core.EntityContext.Partition(partition);

    /// <summary>
    /// Saves entity to vector store only (embeddings + metadata).
    /// Does NOT save to relational store - use model.Save() separately if needed.
    /// </summary>
    public static Task Save(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => VectorData<TEntity>.Save(entity, vector, metadata, ct);

    /// <summary>
    /// Saves entity to vector store only (embeddings + metadata).
    /// Does NOT save to relational store - use model.Save() separately if needed.
    /// </summary>
    public static Task Save(TEntity entity, float[] vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => VectorData<TEntity>.Save(entity, new ReadOnlyMemory<float>(vector), metadata, ct);

    /// <summary>
    /// Saves multiple entities to vector store only (batch operation).
    /// Does NOT save to relational store - use model.Save() for each entity if needed.
    /// </summary>
    public static Task<int> Save(IEnumerable<VectorData<TEntity>.VectorEntity> items, CancellationToken ct = default)
        => VectorData<TEntity>.Save(items, ct);

    /// <summary>
    /// Convenience helper: Saves entity to BOTH relational store (via model.Save()) AND vector store.
    /// </summary>
    public static Task SaveWithVector(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => VectorData<TEntity>.SaveWithVector(entity, vector, metadata, ct);

    /// <summary>
    /// Convenience helper: Saves entity to BOTH relational store (via model.Save()) AND vector store.
    /// </summary>
    public static Task SaveWithVector(TEntity entity, float[] vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => VectorData<TEntity>.SaveWithVector(entity, new ReadOnlyMemory<float>(vector), metadata, ct);

    /// <summary>
    /// Convenience helper: Saves multiple entities to BOTH relational store AND vector store (batch operation).
    /// </summary>
    public static Task<BatchResult> SaveWithVector(IEnumerable<VectorData<TEntity>.VectorEntity> items, CancellationToken ct = default)
        => VectorData<TEntity>.SaveWithVector(items, ct);

    // Save a single vector point by ID; convenience overload
    public static Task Save(string id, float[] embedding, object? metadata = null, CancellationToken ct = default)
        => Repo.UpsertAsync(id, embedding, metadata, ct);

    // Save a single vector point; returns affected count (0|1)
    public static Task<int> Save((string Id, float[] Embedding, object? Metadata) item, CancellationToken ct = default)
        => VectorData<TEntity>.UpsertManyAsync(new[] { item }, ct);

    // Save a batch of vector points; returns total affected
    public static Task<int> Save(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        => VectorData<TEntity>.UpsertManyAsync(items, ct);

    // Delete one or many
    public static Task<bool> Delete(string id, CancellationToken ct = default)
        => Repo.DeleteAsync(id, ct);

    public static Task<int> Delete(IEnumerable<string> ids, CancellationToken ct = default)
        => Repo.DeleteManyAsync(ids, ct);

    // Ensure backing vector index exists (if provider supports it)
    public static Task EnsureCreated(CancellationToken ct = default)
        => Repo.VectorEnsureCreatedAsync(ct);

    // Index maintenance via instructions (optional; provider-dependent)
    public static async Task<bool> Clear(CancellationToken ct = default)
    {
        if (Repo is Koan.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<bool>(new Koan.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexClear), ct);
        throw new InvalidOperationException("Clear requires instruction support by the vector provider.");
    }

    /// <summary>
    /// Flush (clear) the entire vector index. This is a destructive operation that deletes all vectors.
    /// Each adapter implements this according to its provider's capabilities.
    /// </summary>
    public static Task Flush(CancellationToken ct = default)
        => Repo.FlushAsync(ct);

    public static async Task<bool> Rebuild(CancellationToken ct = default)
    {
        if (Repo is Koan.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<bool>(new Koan.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexRebuild), ct);
        throw new InvalidOperationException("Rebuild requires instruction support by the vector provider.");
    }

    public static async Task<int> Stats(CancellationToken ct = default)
    {
        if (Repo is Koan.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<int>(new Koan.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexStats), ct);
        throw new InvalidOperationException("Stats requires instruction support by the vector provider.");
    }

    public static VectorCapabilities GetCapabilities()
        => Repo is IVectorCapabilities caps ? caps.Capabilities : VectorCapabilities.None;

    /// <summary>
    /// Retrieves the embedding vector for a specific entity by ID.
    /// Returns null if no vector exists for the given ID.
    /// </summary>
    public static Task<float[]?> GetEmbedding(string id, CancellationToken ct = default)
        => Repo.GetEmbeddingAsync(id, ct);

    /// <summary>
    /// Retrieves embedding vectors for multiple entities by IDs.
    /// Returns a dictionary mapping IDs to embeddings. Missing IDs are omitted.
    /// </summary>
    public static Task<Dictionary<string, float[]>> GetEmbeddings(IEnumerable<string> ids, CancellationToken ct = default)
        => Repo.GetEmbeddingsAsync(ids, ct);

    // Search overloads
    public static Task<VectorQueryResult<string>> Search(VectorQueryOptions options, CancellationToken ct = default)
        => VectorData<TEntity>.SearchAsync(options, ct);

    /// <summary>
    /// Unified search interface supporting both pure vector and hybrid (semantic + keyword) search.
    /// </summary>
    /// <param name="vector">Query vector embedding (always required).</param>
    /// <param name="text">Optional text for hybrid BM25 keyword matching. When provided, enables hybrid search.</param>
    /// <param name="alpha">Optional semantic vs keyword weight. 0.0=keyword only, 1.0=semantic only, 0.5=balanced (default).</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="filter">Optional provider-specific filter.</param>
    /// <param name="vectorName">Optional vector name for multi-vector entities.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Vector query results with similarity scores.</returns>
    public static Task<VectorQueryResult<string>> Search(
        float[] vector,
        string? text = null,
        double? alpha = null,
        int? topK = null,
        object? filter = null,
        string? vectorName = null,
        CancellationToken ct = default)
        => VectorData<TEntity>.SearchAsync(new VectorQueryOptions(
            Query: vector,
            TopK: topK,
            Filter: filter,
            VectorName: vectorName,
            SearchText: text,
            Alpha: alpha
        ), ct);
}
