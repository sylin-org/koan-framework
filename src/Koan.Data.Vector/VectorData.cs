using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.Vector;

public static class VectorData<TEntity>
    where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
    => (Koan.Core.Hosting.App.AppHost.Current?.GetService<IVectorService>()?.TryGetRepository<TEntity, string>())
           ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    /// <summary>
    /// Saves entity to vector store only (embeddings + metadata).
    /// Does NOT save to relational store - use model.Save() separately if needed.
    /// </summary>
    public static async Task Save(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(entity);
        await Repo.UpsertAsync(entity.Id, vector.ToArray(), metadata, ct);
    }

    /// <summary>
    /// Convenience helper: Saves entity to BOTH relational store (via model.Save()) AND vector store.
    /// Equivalent to: await model.Save(ct); await Vector&lt;T&gt;.Save(model, vector, metadata, ct);
    /// </summary>
    public static async Task SaveWithVector(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(entity);

        if (VectorWorkflow<TEntity>.IsAvailable())
        {
            var payload = CloneMetadata(metadata);
            await VectorWorkflow<TEntity>.Save(entity, vector.ToArray(), payload, null, ct);
            return;
        }

        await entity.Save(ct);
        await Repo.UpsertAsync(entity.Id, vector.ToArray(), metadata, ct);
    }

    /// <summary>
    /// Saves multiple entities to vector store only (batch operation).
    /// Does NOT save to relational store - use model.Save() for each entity if needed.
    /// </summary>
    public static async Task<int> Save(IEnumerable<VectorEntity> items, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(items);
        var list = items as IList<VectorEntity> ?? items.ToList();

        if (list.Count == 0)
            return 0;

        var vectors = list.Select(x => (x.Entity.Id, x.Vector.ToArray(), (object?)x.Metadata)).ToList();
        return await Repo.UpsertManyAsync(vectors, ct);
    }

    /// <summary>
    /// Convenience helper: Saves multiple entities to BOTH relational store AND vector store (batch operation).
    /// Equivalent to: foreach(var item in items) await item.Entity.Save(ct); + Vector&lt;T&gt;.Save(items, ct);
    /// </summary>
    public static async Task<BatchResult> SaveWithVector(IEnumerable<VectorEntity> items, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(items);
        var list = items as IList<VectorEntity> ?? items.ToList();

        if (VectorWorkflow<TEntity>.IsAvailable())
        {
            if (list.Count == 0)
            {
                return new BatchResult(0, 0, 0);
            }

            var mapped = list.Select(x => (x.Entity, x.Vector.ToArray(), (object?)CloneMetadata(x.Metadata))).ToList();
            var result = await VectorWorkflow<TEntity>.SaveMany(mapped, null, ct);
            return new BatchResult(result.Documents, 0, 0);
        }

        if (list.Count == 0)
        {
            return new BatchResult(0, 0, 0);
        }

        // Save entities to relational store
        var documents = list.Select(x => x.Entity).ToList();
        var affected = 0;
        foreach (var entity in documents)
        {
            await entity.Save(ct);
            affected++;
        }

        // Save vectors to vector store
        var vectors = list.Select(x => (x.Entity.Id, x.Vector.ToArray(), (object?)x.Metadata)).ToList();
        await Repo.UpsertManyAsync(vectors, ct);

        return new BatchResult(affected, 0, 0);
    }

    public static Task<int> UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        => Repo.UpsertManyAsync(items, ct);

    public static Task<VectorQueryResult<string>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
        => Repo.SearchAsync(options, ct);

    private static Dictionary<string, object?>? CloneMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var copy = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in metadata)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            copy[entry.Key] = entry.Value;
        }

        return copy;
    }

    public readonly record struct VectorEntity(
        TEntity Entity,
        ReadOnlyMemory<float> Vector,
        string? Anchor = null,
        IReadOnlyDictionary<string, object>? Metadata = null);
}