using System;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Abstractions;

public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default);
    Task<int> CountAsync(object? query, CancellationToken ct = default);
    Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);

    Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default);
    // Deletes all entities in the current set; should return the number of deleted records
    Task<int> DeleteAllAsync(CancellationToken ct = default);

    IBatchSet<TEntity, TKey> CreateBatch();
}

public interface IBatchSet<TEntity, TKey>
{
    IBatchSet<TEntity, TKey> Add(TEntity entity);
    IBatchSet<TEntity, TKey> Update(TEntity entity);
    // Convenience: queue an update by id and a mutation action to apply before saving
    IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate);
    IBatchSet<TEntity, TKey> Delete(TKey id);
    IBatchSet<TEntity, TKey> Clear();
    Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default);
}

public sealed record BatchOptions(bool RequireAtomic = false, string? IdempotencyKey = null, int? MaxItems = null);
public sealed record BatchResult(int Added, int Updated, int Deleted);

// Optional query capability: raw string query (e.g., SQL, JSON filter)
public interface IStringQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default);
    // Optional overload to supply parameters for safe binding
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default);
    Task<int> CountAsync(string query, CancellationToken ct = default);
    Task<int> CountAsync(string query, object? parameters, CancellationToken ct = default);
}

// Optional query capability: LINQ predicate
public interface ILinqQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
}
