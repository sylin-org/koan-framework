using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;

namespace Sora.Data.Core;

/// <summary>
/// High-level convenience extensions for aggregates and collections.
/// Wraps repository calls with concise verbs (Upsert, Save, Remove),
/// keeps parity with domain static helpers, and hides service lookups.
/// </summary>
public static class AggregateExtensions
{
    private static IDataService DataService()
        => SoraApp.Current?.GetService<IDataService>()
            ?? throw new System.InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");

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
    var id = Sora.Data.Core.Metadata.AggregateMetadata.GetIdValue(model) ?? throw new System.InvalidOperationException("Model has no identifier");
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

// Wrapper for the common string key case: Data<T>
public static class Data<TEntity>
    where TEntity : class, IEntity<string>
{
    public static Task<TEntity?> GetAsync(string id, CancellationToken ct = default)
        => Data<TEntity, string>.GetAsync(id, ct);

    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Data<TEntity, string>.All(ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Data<TEntity, string>.Query(query, ct);
    public static Task<int> Count(CancellationToken ct = default)
        => Data<TEntity, string>.CountAllAsync(ct);
    public static Task<int> Count(string query, CancellationToken ct = default)
        => Data<TEntity, string>.CountAsync(query, ct);

    public static Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        => Data<TEntity, string>.DeleteAsync(id, ct);

    public static Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
        => Data<TEntity, string>.DeleteManyAsync(ids, ct);

    public static Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
        => Data<TEntity, string>.UpsertManyAsync(models, ct);

    public static IBatchSet<TEntity, string> Batch()
        => Data<TEntity, string>.Batch();

    // Instruction execution sugar for common string-key case
    public static Task<TResult> Execute<TResult>(Instruction instruction, CancellationToken ct = default)
        => Data<TEntity, string>.Execute<TResult>(instruction, ct);

    public static Task<TResult> Execute<TResult>(Instruction instruction, IDataService data, CancellationToken ct = default)
        => Data<TEntity, string>.Execute<TResult>(instruction, data, ct);

    // Default NonQuery sugar using raw SQL (returns affected rows)
    public static Task<int> Execute(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
        => Data<TEntity, string>.Execute(sql, data, parameters, ct);

    // Raw SQL sugar using SoraApp.Current (int => NonQuery by default)
    public static Task<int> Execute(string sql, CancellationToken ct = default)
        => Data<TEntity, string>.Execute(sql, ct);

    // Generic raw SQL sugar: int => NonQuery, otherwise Scalar
    public static Task<TResult> Execute<TResult>(string sql, CancellationToken ct = default)
        => Data<TEntity, string>.Execute<TResult>(sql, ct);

    public static Task<TResult> Execute<TResult>(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
        => Data<TEntity, string>.Execute<TResult>(sql, data, parameters, ct);
}

// Static facade: Data<TEntity,TKey>.GetAsync/QueryAsync/Batch()
public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static IDataRepository<TEntity, TKey> Repo
        => SoraApp.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()
            ?? throw new System.InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");

    public static IQueryCapabilities QueryCaps
        => Repo as IQueryCapabilities ?? new Caps(QueryCapabilities.None);

    public static IWriteCapabilities WriteCaps
        => Repo as IWriteCapabilities ?? new WriteCapsImpl(WriteCapabilities.None);

    public static Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default) => Repo.GetAsync(id, ct);
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default) => Repo.QueryAsync(null, ct);
    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => (Repo as ILinqQueryRepository<TEntity, TKey>)?.QueryAsync(predicate, ct)
           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => (Repo as IStringQueryRepository<TEntity, TKey>)?.QueryAsync(query, ct)
           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    public static Task<int> CountAllAsync(CancellationToken ct = default)
        => Repo.CountAsync(null, ct);
    public static Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => (Repo as ILinqQueryRepository<TEntity, TKey>)?.CountAsync(predicate, ct)
           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    public static Task<int> CountAsync(string query, CancellationToken ct = default)
        => (Repo as IStringQueryRepository<TEntity, TKey>)?.CountAsync(query, ct)
           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    public static Task<bool> DeleteAsync(TKey id, CancellationToken ct = default) => Repo.DeleteAsync(id, ct);
    public static Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.DeleteManyAsync(ids, ct);
    public static Task<int> DeleteAllAsync(CancellationToken ct = default) => Repo.DeleteAllAsync(ct);
    public static Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default) => Repo.UpsertManyAsync(models, ct);
    public static IBatchSet<TEntity, TKey> Batch() => Repo.CreateBatch();

    // Set-scoped helpers (ambient via DataSetContext)
    public static IDisposable WithSet(string? set) => DataSetContext.With(set);

    public static Task<TEntity?> GetAsync(TKey id, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.GetAsync(id, ct); }

    public static Task<IReadOnlyList<TEntity>> All(string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.QueryAsync((object?)null, ct); }
    public static Task<int> CountAllAsync(string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.CountAsync((object?)null, ct); }

    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return (Repo as ILinqQueryRepository<TEntity, TKey>)?.QueryAsync(predicate, ct)
            ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository."); }
    public static Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return (Repo as ILinqQueryRepository<TEntity, TKey>)?.CountAsync(predicate, ct)
            ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository."); }

    public static Task<IReadOnlyList<TEntity>> Query(string query, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return (Repo as IStringQueryRepository<TEntity, TKey>)?.QueryAsync(query, ct)
            ?? throw new System.NotSupportedException("String queries are not supported by this repository."); }
    public static Task<int> CountAsync(string query, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return (Repo as IStringQueryRepository<TEntity, TKey>)?.CountAsync(query, ct)
            ?? throw new System.NotSupportedException("String queries are not supported by this repository."); }

    public static Task<TEntity> UpsertAsync(TEntity model, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.UpsertAsync(model, ct); }

    public static Task<bool> DeleteAsync(TKey id, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.DeleteAsync(id, ct); }

    public static Task<int> UpsertManyAsync(IEnumerable<TEntity> models, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.UpsertManyAsync(models, ct); }

    public static Task<int> DeleteManyAsync(IEnumerable<TKey> ids, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.DeleteManyAsync(ids, ct); }

    public static async Task<int> Delete(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set);
        if (Repo is ILinqQueryRepository<TEntity, TKey> linq)
        {
            var items = await linq.QueryAsync(predicate, ct).ConfigureAwait(false);
            var ids = items.Select(e => e.Id);
            return await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
        else
        {
            var all = await Repo.QueryAsync((object?)null, ct).ConfigureAwait(false);
            var filtered = all.AsQueryable().Where(predicate).ToList();
            var ids = filtered.Select(e => e.Id);
            return await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
    }

    // Instruction execution sugar via IDataService-backed repository
    public static Task<TResult> Execute<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        var ds = SoraApp.Current?.GetService<IDataService>()
            ?? throw new System.InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instruction, ct);
    }

    public static Task<TResult> Execute<TResult>(Instruction instruction, IDataService data, CancellationToken ct = default)
        => DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instruction, ct);

    // Raw SQL sugar helpers
    public static Task<int> Execute(string sql, CancellationToken ct = default)
    {
        var ds = SoraApp.Current?.GetService<IDataService>()
            ?? throw new System.InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");
        return DataServiceExecuteExtensions.Execute<TEntity, int>(ds, InstructionSql.NonQuery(sql), ct);
    }

    public static Task<int> Execute(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
        => DataServiceExecuteExtensions.Execute<TEntity, int>(data, InstructionSql.NonQuery(sql, parameters), ct);

    public static Task<TResult> Execute<TResult>(string sql, CancellationToken ct = default)
    {
        var ds = SoraApp.Current?.GetService<IDataService>()
            ?? throw new System.InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");
        var instr = typeof(TResult) == typeof(int)
            ? InstructionSql.NonQuery(sql)
            : InstructionSql.Scalar(sql);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instr, ct);
    }

    public static Task<TResult> Execute<TResult>(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
    {
        var instr = typeof(TResult) == typeof(int)
            ? InstructionSql.NonQuery(sql, parameters)
            : InstructionSql.Scalar(sql, parameters);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instr, ct);
    }

    private sealed record Caps(QueryCapabilities Cap) : IQueryCapabilities { public QueryCapabilities Capabilities => Cap; }
    private sealed record WriteCapsImpl(WriteCapabilities Val) : IWriteCapabilities { public WriteCapabilities Writes => Val; }

    // ------------------------------
    // Set migration helpers (copy/move/clear/replace) + fluent builder
    // ------------------------------

    public static Task<int> ClearSet(string set, CancellationToken ct = default)
        => Delete(Expression.Lambda<Func<TEntity, bool>>(Expression.Constant(true), Expression.Parameter(typeof(TEntity), "_")), set, ct);

    public static async Task<int> CopySet(
        string fromSet,
        string toSet,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromSet, toSet, StringComparison.Ordinal)) return 0;
        using var _from = WithSet(fromSet);
        var source = predicate is null
            ? await Repo.QueryAsync((object?)null, ct).ConfigureAwait(false)
            : await (Repo as ILinqQueryRepository<TEntity, TKey>)!.QueryAsync(predicate, ct).ConfigureAwait(false);
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            using var _to = WithSet(toSet);
            total += await Repo.UpsertManyAsync(items, ct).ConfigureAwait(false);
        }
        return total;
    }

    public static async Task<int> MoveSet(
        string fromSet,
        string toSet,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromSet, toSet, StringComparison.Ordinal)) return 0;
        using var _from = WithSet(fromSet);
        var source = predicate is null
            ? await Repo.QueryAsync((object?)null, ct).ConfigureAwait(false)
            : await (Repo as ILinqQueryRepository<TEntity, TKey>)!.QueryAsync(predicate, ct).ConfigureAwait(false);
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            // Upsert into target set
            using var _to = WithSet(toSet);
            total += await Repo.UpsertManyAsync(items, ct).ConfigureAwait(false);
            // Delete the moved ids from source set
            using var _back = WithSet(fromSet);
            var ids = items.Select(e => e.Id);
            await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
        return total;
    }

    public static async Task<int> ReplaceSet(
        string targetSet,
        IEnumerable<TEntity> items,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        await ClearSet(targetSet, ct).ConfigureAwait(false);
        var total = 0;
        foreach (var chunk in items.Chunk(Math.Max(1, batchSize)))
        {
            using var _ = WithSet(targetSet);
            total += await Repo.UpsertManyAsync(chunk, ct).ConfigureAwait(false);
        }
        return total;
    }

    // Fluent builder: Data<TEntity,TKey>.MoveFrom("backup").Where(...).Map(...).Copy().BatchSize(1000).To("root");
    public static SetMoveBuilder<TEntity, TKey> MoveFrom(string fromSet) => new(fromSet);
}

// Fluent move/copy builder
public sealed class SetMoveBuilder<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly string _from;
    private Expression<Func<TEntity, bool>>? _predicate;
    private Func<TEntity, TEntity>? _map;
    private bool _copyOnly;
    private int _batchSize = 500;

    internal SetMoveBuilder(string from) => _from = from;
    public SetMoveBuilder<TEntity, TKey> Where(Expression<Func<TEntity, bool>> predicate) { _predicate = predicate; return this; }
    public SetMoveBuilder<TEntity, TKey> Map(Func<TEntity, TEntity> transform) { _map = transform; return this; }
    public SetMoveBuilder<TEntity, TKey> Copy() { _copyOnly = true; return this; }
    public SetMoveBuilder<TEntity, TKey> BatchSize(int size) { if (size > 0) _batchSize = size; return this; }
    public Task<int> To(string toSet, CancellationToken ct = default)
        => _copyOnly
            ? Data<TEntity, TKey>.CopySet(_from, toSet, _predicate, _map, _batchSize, ct)
            : Data<TEntity, TKey>.MoveSet(_from, toSet, _predicate, _map, _batchSize, ct);
}

// Instance-level sugar: model.MoveToSet("target", fromSet: null (ambient), copy: false)
public static class EntitySetMoveExtensions
{
    public static async Task MoveToSet<TEntity, TKey>(this TEntity model, string toSet, string? fromSet = null, bool copy = false, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Upsert into target
        await Data<TEntity, TKey>.UpsertAsync(model, toSet, ct).ConfigureAwait(false);
        if (!copy)
        {
            var from = fromSet ?? DataSetContext.Current;
            await Data<TEntity, TKey>.DeleteAsync(model.Id, from ?? "root", ct).ConfigureAwait(false);
        }
    }
}
