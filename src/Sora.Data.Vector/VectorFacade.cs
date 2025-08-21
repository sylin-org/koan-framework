using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Vector;

// SoC-friendly vector facade: lives in Vector assembly; no changes to core Entity<>
public class Vector<TEntity> where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
        => (((Sora.Core.SoraApp.Current?.GetService(typeof(IVectorService))) as IVectorService)?.TryGetRepository<TEntity, string>())
            ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    private static IVectorSearchRepository<TEntity, string>? TryRepo
        => ((Sora.Core.SoraApp.Current?.GetService(typeof(IVectorService))) as IVectorService)?.TryGetRepository<TEntity, string>();

    public static bool IsAvailable => TryRepo is not null;

    // Save a single vector point; convenience overload
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
        if (Repo is Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<bool>(new Sora.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexClear), ct);
        throw new InvalidOperationException("Clear requires instruction support by the vector provider.");
    }

    public static async Task<bool> Rebuild(CancellationToken ct = default)
    {
        if (Repo is Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<bool>(new Sora.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexRebuild), ct);
        throw new InvalidOperationException("Rebuild requires instruction support by the vector provider.");
    }

    public static async Task<int> Stats(CancellationToken ct = default)
    {
        if (Repo is Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<int>(new Sora.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexStats), ct);
        throw new InvalidOperationException("Stats requires instruction support by the vector provider.");
    }

    public static VectorCapabilities GetCapabilities()
        => Repo is IVectorCapabilities caps ? caps.Capabilities : VectorCapabilities.None;

    // Search overloads
    public static Task<Sora.Data.Vector.Abstractions.VectorQueryResult<string>> Search(Sora.Data.Vector.Abstractions.VectorQueryOptions options, CancellationToken ct = default)
        => VectorData<TEntity>.SearchAsync(options, ct);

    public static Task<Sora.Data.Vector.Abstractions.VectorQueryResult<string>> Search(float[] query, int? topK = null, object? filter = null, string? vectorName = null, CancellationToken ct = default)
        => VectorData<TEntity>.SearchAsync(new VectorQueryOptions(query, TopK: topK, Filter: filter, VectorName: vectorName), ct);
}
