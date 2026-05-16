using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Schema;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Core.Hosting.App;

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
    public static Task Save(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(entity);

        var context = Koan.Data.Core.EntityContext.Current;

        // Check for active transaction
        if (context?.TransactionCoordinator != null)
        {
            // Defer execution - track in transaction
            context.TransactionCoordinator.TrackVectorSave<TEntity, string>(
                entity.Id,
                vector,
                metadata,
                context);

            return Task.CompletedTask;  // Deferred
        }

        // No transaction - execute immediately
        var normalized = NormalizeMetadata(metadata);
        return Repo.Upsert(entity.Id, vector.ToArray(), normalized, ct);
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
            var payload = NormalizeMetadata(metadata);
            await VectorWorkflow<TEntity>.Save(entity, vector.ToArray(), payload, null, ct);
            return;
        }

        var context = Koan.Data.Core.EntityContext.Current;

        if (context?.TransactionCoordinator != null)
        {
            // In transaction: defer BOTH operations
            await entity.Save(ct);  // Defers via Data<TEntity, string>
            await Save(entity, vector, metadata, ct);  // Defers via our transaction-aware Save above
            // Both execute atomically on commit
            return;
        }

        // Not in transaction: execute sequentially with detailed error handling
        bool entitySaved = false;
        try
        {
            await entity.Save(ct);
            entitySaved = true;

            var normalized = NormalizeMetadata(metadata);
            await Repo.Upsert(entity.Id, vector.ToArray(), normalized, ct);
            // Both succeeded
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (entitySaved)
            {
                // Entity saved, vector failed - critical inconsistency
                throw new VectorCoordinationException(
                    $"Vector save failed after entity was persisted. " +
                    $"Entity {entity.Id} exists in database but has no vector representation. " +
                    $"Use background re-embedding to recover.",
                    entity.Id,
                    entitySaved: true,
                    vectorSaved: false,
                    ex);
            }
            else
            {
                // Entity save failed - clean failure, nothing persisted
                throw;
            }
        }
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

        var vectors = list.Select(x => (x.Entity.Id, x.Vector.ToArray(), (object?)NormalizeMetadata(x.Metadata))).ToList();
        return await Repo.UpsertMany(vectors, ct);
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

            var mapped = list.Select(x => (x.Entity, x.Vector.ToArray(), (object?)NormalizeMetadata(x.Metadata))).ToList();
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
        var vectors = list.Select(x => (x.Entity.Id, x.Vector.ToArray(), (object?)NormalizeMetadata(x.Metadata))).ToList();
        await Repo.UpsertMany(vectors, ct);

        return new BatchResult(affected, 0, 0);
    }

    public static Task<int> UpsertMany(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        var materialized = items as IList<(string Id, float[] Embedding, object? Metadata)> ?? items.ToList();
        if (materialized.Count == 0)
        {
            return Task.FromResult(0);
        }

        var normalized = materialized
            .Select(x => (x.Id, x.Embedding, (object?)NormalizeMetadata(x.Metadata)))
            .ToList();

        return Repo.UpsertMany(normalized, ct);
    }

    public static Task<VectorQueryResult<string>> Search(VectorQueryOptions options, CancellationToken ct = default)
        => Repo.Search(options, ct);

    private static IDictionary<string, object?>? NormalizeMetadata(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var descriptor = TryGetDescriptor();
        if (descriptor is not null)
        {
            var projected = descriptor.ProjectMetadata(metadata);
            if (projected is not null)
            {
                return ToDictionary(projected);
            }
        }

        switch (metadata)
        {
            case IDictionary<string, object?> dict:
                return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
            case IReadOnlyDictionary<string, object?> readOnly:
                return ToDictionary(readOnly);
            default:
                return null;
        }
    }

    private static Dictionary<string, object?> ToDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var copy = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            copy[entry.Key] = entry.Value;
        }

        return copy;
    }

    private static VectorSchemaDescriptor? TryGetDescriptor()
    {
        var provider = AppHost.Current;
        if (provider is null)
        {
            return null;
        }

        var registry = provider.GetService<VectorSchemaRegistry>();
        return registry?.TryGet<TEntity, string>();
    }

    public readonly record struct VectorEntity(
        TEntity Entity,
        ReadOnlyMemory<float> Vector,
        string? Anchor = null,
        IReadOnlyDictionary<string, object>? Metadata = null);
}