using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>
/// High-level convenience extensions for aggregates and collections.
/// Wraps repository calls with concise verbs (Upsert, Save, Remove),
/// keeps parity with domain static helpers, and hides service lookups.
/// </summary>
public static class AggregateExtensions
{
    private static IDataService DataService()
        => Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()
            ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");

    // Instance-level convenience: model.Upsert() (generic key)
    /// <summary>
    /// Insert or update a model for the aggregate using its configured repository.
    /// Ensures identifiers via the identity manager and returns the saved entity.
    /// </summary>
    public static Task<TEntity> Upsert<TEntity, TKey>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => DataService().GetRepository<TEntity, TKey>().UpsertAsync(model, ct);

    // Alias: model.Save() -> Upsert (generic key)
    /// <summary>
    /// Alias for Upsert for generic-key entities; intended as a friendly verb in app code.
    /// </summary>
    public static Task<TEntity> Save<TEntity, TKey>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => model.Upsert<TEntity, TKey>(ct);

    // Instance-level convenience: model.Upsert("set") (generic key)
    /// <summary>
    /// Insert or update a model into a specific logical set for the aggregate using its configured repository.
    /// Routes storage to BaseName#&lt;set&gt; via DataSetContext and StorageNameRegistry.
    /// </summary>
    public static Task<TEntity> Upsert<TEntity, TKey>(this TEntity model, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        using var _ = Data<TEntity, TKey>.WithSet(set);
        return DataService().GetRepository<TEntity, TKey>().UpsertAsync(model, ct);
    }

    // Alias: model.Save("set") -> Upsert("set") (generic key)
    /// <summary>
    /// Alias for Upsert into a specific set for generic-key entities.
    /// </summary>
    public static Task<TEntity> Save<TEntity, TKey>(this TEntity model, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => model.Upsert<TEntity, TKey>(set, ct);

    // Non-generic convenience for the common string key case
    /// <summary>
    /// Upsert for string-keyed entities without specifying TKey.
    /// </summary>
    public static Task<TEntity> Upsert<TEntity>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => DataService().GetRepository<TEntity, string>().UpsertAsync(model, ct);

    // Alias: model.Save() -> Upsert (string key convenience)
    /// <summary>
    /// Save alias for string-keyed entities.
    /// </summary>
    public static Task<TEntity> Save<TEntity>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => model.Upsert(ct);

    // Non-generic convenience: model.Upsert("set") (string key)
    /// <summary>
    /// Upsert a string-keyed entity into a specific logical set.
    /// </summary>
    public static Task<TEntity> Upsert<TEntity>(this TEntity model, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        using var _ = Data<TEntity, string>.WithSet(set);
        return DataService().GetRepository<TEntity, string>().UpsertAsync(model, ct);
    }

    // Alias: model.Save("set") -> Upsert("set") (string key)
    /// <summary>
    /// Save alias for string-keyed entities into a specific set.
    /// </summary>
    public static Task<TEntity> Save<TEntity>(this TEntity model, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => model.Upsert(set, ct);

    // Return only the identifier after upsert (generic)
    /// <summary>
    /// Upsert and return only the identifier (generic key).
    /// </summary>
    public static async Task<TKey> UpsertId<TEntity, TKey>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => (await DataService().GetRepository<TEntity, TKey>().UpsertAsync(model, ct)).Id;

    // Convenience for string key: UpsertId()
    /// <summary>
    /// Upsert and return only the identifier (string key convenience).
    /// </summary>
    public static Task<string> UpsertId<TEntity>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => model.UpsertId<TEntity, string>(ct);

    // Non-generic instance Upsert() with runtime inference
    /// <summary>
    /// Runtime Upsert for unknown entity types; reflects the repository and UpsertAsync method.
    /// </summary>
    public static async Task<object?> Upsert(this object model, CancellationToken ct = default)
    {
        if (model is null) throw new System.ArgumentNullException(nameof(model));
        var (aggType, keyType) = ResolveAggregateContract(model.GetType());
        var data = DataService();
        var getRepo = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository))!;
        var repo = getRepo.MakeGenericMethod(aggType, keyType).Invoke(data, System.Array.Empty<object>())!;
        var upsert = repo.GetType().GetMethod("UpsertAsync")!;
        var task = (Task)upsert.Invoke(repo, new object[] { model, ct })!;
        await task.ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result");
        return resultProp?.GetValue(task);
    }

    // Non-generic instance Delete() using Identifier attribute (or Id) with cached metadata
    /// <summary>
    /// Runtime Delete for unknown entity types; uses Identifier metadata or Id to locate the key.
    /// </summary>
    public static async Task<bool> Delete(this object model, CancellationToken ct = default)
    {
        if (model is null) throw new System.ArgumentNullException(nameof(model));
        var type = model.GetType();
        var (aggType, keyType) = ResolveAggregateContract(type);
        var id = AggregateMetadata.GetIdValue(model) ?? throw new System.InvalidOperationException("Model has no identifier");
        var data = DataService();
        var getRepo = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository))!;
        var repo = getRepo.MakeGenericMethod(aggType, keyType).Invoke(data, System.Array.Empty<object>())!;
        var del = repo.GetType().GetMethod("DeleteAsync")!;
        var task = (Task)del.Invoke(repo, new object[] { id, ct })!;
        await task.ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result");
        return resultProp is not null && resultProp.GetValue(task) is bool b && b;
    }

    // Typed instance Remove mirroring Entity<TEntity,TKey>.Remove()
    /// <summary>
    /// Remove a single entity by its id (typed convenience mirroring Entity.Remove).
    /// </summary>
    public static Task<bool> Remove<TEntity, TKey>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.DeleteAsync(model.Id, ct);

    // String-key convenience
    /// <summary>
    /// Remove a single entity (string-key convenience).
    /// </summary>
    public static Task<bool> Remove<TEntity>(this TEntity model, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => Data<TEntity, string>.DeleteAsync(model.Id, ct);

    private static (Type Aggregate, Type Key) ResolveAggregateContract(Type t)
    {
        foreach (var itf in t.GetInterfaces())
        {
            if (itf.IsGenericType && itf.GetGenericTypeDefinition() == typeof(IEntity<>))
                return (t, itf.GetGenericArguments()[0]);
        }
        throw new System.InvalidOperationException($"Type {t.Name} does not implement IEntity<TKey>");
    }

    // IEnumerable<T> conveniences
    // Bulk save (generic key)
    /// <summary>
    /// Bulk upsert a collection; providers may optimize to native bulk operations.
    /// </summary>
    public static Task<int> Save<TEntity, TKey>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.UpsertManyAsync(models, ct);

    // String-key convenience delegates to generic (cannot infer TKey from constraint)
    /// <summary>
    /// Bulk upsert (string-key convenience).
    /// </summary>
    public static Task<int> Save<TEntity>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => Data<TEntity, string>.UpsertManyAsync(models, ct);

    // Bulk upsert into a specific set (generic key)
    /// <summary>
    /// Bulk upsert into a specific logical set for the aggregate (generic key).
    /// </summary>
    public static Task<int> Save<TEntity, TKey>(this IEnumerable<TEntity> models, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.UpsertManyAsync(models, set, ct);

    // Bulk upsert into a specific set (string key convenience)
    /// <summary>
    /// Bulk upsert into a specific logical set (string key convenience).
    /// </summary>
    public static Task<int> Save<TEntity>(this IEnumerable<TEntity> models, string set, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => Data<TEntity, string>.UpsertManyAsync(models, set, ct);

    // Bulk remove (delete many) by models collection
    /// <summary>
    /// Bulk remove a collection by projecting ids.
    /// </summary>
    public static Task<int> Remove<TEntity, TKey>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.DeleteManyAsync(models.Select(m => m.Id), ct);

    // String-key convenience delegates to generic
    /// <summary>
    /// Bulk remove (string-key convenience).
    /// </summary>
    public static Task<int> Remove<TEntity>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => Data<TEntity, string>.DeleteManyAsync(models.Select(m => m.Id), ct);

    // Replace all contents with provided models
    /// <summary>
    /// Replace the entire set with the provided models: delete all then upsert.
    /// Intended for dev/test seeding and idempotent resets; avoid on large datasets.
    /// </summary>
    public static async Task<int> SaveReplacing<TEntity, TKey>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var existing = await Data<TEntity, TKey>.All(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            var ids = existing.Select(e => e.Id);
            await Data<TEntity, TKey>.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
        return await Data<TEntity, TKey>.UpsertManyAsync(models, ct).ConfigureAwait(false);
    }

    // String-key convenience delegates to generic
    /// <summary>
    /// String-key convenience for SaveReplacing.
    /// </summary>
    public static Task<int> SaveReplacing<TEntity>(this IEnumerable<TEntity> models, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => SaveReplacing<TEntity, string>(models, ct);

    // Convert enumerable to a pre-filled batch (generic key)
    /// <summary>
    /// Convert a collection to a pre-filled batch by queuing add operations.
    /// </summary>
    public static IBatchSet<TEntity, TKey> AsBatch<TEntity, TKey>(this IEnumerable<TEntity> models)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var batch = Data<TEntity, TKey>.Batch();
        foreach (var m in models) batch.Add(m);
        return batch;
    }

    // Convert enumerable to a pre-filled batch (string key convenience)
    /// <summary>
    /// String-key convenience for AsBatch.
    /// </summary>
    public static IBatchSet<TEntity, string> AsBatch<TEntity>(this IEnumerable<TEntity> models)
        where TEntity : class, IEntity<string>
    {
        var batch = Data<TEntity, string>.Batch();
        foreach (var m in models) batch.Add(m);
        return batch;
    }

    // Vice versa helper: add many entities to an existing batch
    /// <summary>
    /// Add many entities to an existing batch for fluent composition.
    /// </summary>
    public static IBatchSet<TEntity, TKey> AddRange<TEntity, TKey>(this IBatchSet<TEntity, TKey> batch, IEnumerable<TEntity> models)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        foreach (var m in models) batch.Add(m);
        return batch;
    }
}

// Static facade: Data<TEntity,TKey>.GetAsync/QueryAsync/Batch()

// Fluent move/copy builder

// Instance-level sugar: model.MoveToSet("target", fromSet: null (ambient), copy: false)