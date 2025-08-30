using Sora.Data.Abstractions;

namespace Sora.Data.Core.Model;

// Domain-centric CRTP base with static conveniences, independent of data namespace
public abstract class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    [Identifier]
    public TKey Id { get; set; } = default!;

    // Static conveniences forward to the data facade without exposing its namespace in domain types
    public static Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => Data<TEntity, TKey>.GetAsync(id, ct);
    // Set-aware variants
    public static Task<TEntity?> Get(TKey id, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.GetAsync(id, set, ct);

    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Data<TEntity, TKey>.All(ct);
    public static Task<IReadOnlyList<TEntity>> All(string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.All(set, ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Data<TEntity, TKey>.Query(query, ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.Query(query, set, ct);

    // Streaming (IAsyncEnumerable)
    public static IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default)
        => Data<TEntity, TKey>.AllStream(batchSize, ct);
    public static IAsyncEnumerable<TEntity> QueryStream(string query, int? batchSize = null, CancellationToken ct = default)
        => Data<TEntity, TKey>.QueryStream(query, batchSize, ct);

    // Basic paging helpers (materialized)
    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
        => Data<TEntity, TKey>.FirstPage(size, ct);
    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
        => Data<TEntity, TKey>.Page(page, size, ct);

    // Counts
    public static Task<int> Count(CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(ct);
    public static Task<int> Count(string query, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAsync(query, ct);
    public static Task<int> CountAll(string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(set, ct);
    public static Task<int> Count(string query, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAsync(query, set, ct);

    public static IBatchSet<TEntity, TKey> Batch() => Data<TEntity, TKey>.Batch();

    public static Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => Data<TEntity, TKey>.UpsertManyAsync(models, ct);

    // Removal helpers
    public static Task<bool> Remove(TKey id, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(id, ct);

    public static Task<int> Remove(IEnumerable<TKey> ids, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteManyAsync(ids, ct);

    // Set-aware removal helpers
    public static Task<bool> Remove(TKey id, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(id, set, ct);

    public static Task<int> Remove(IEnumerable<TKey> ids, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteManyAsync(ids, set, ct);

    public static async Task<int> Remove(string query, CancellationToken ct = default)
    {
        var items = await Data<TEntity, TKey>.Query(query, ct);
        var ids = Enumerable.Select<TEntity, TKey>(items, e => e.Id);
        return await Data<TEntity, TKey>.DeleteManyAsync(ids, ct);
    }

    public static async Task<int> Remove(string query, string set, CancellationToken ct = default)
    {
        var items = await Data<TEntity, TKey>.Query(query, set, ct);
        var ids = Enumerable.Select<TEntity, TKey>(items, e => e.Id);
        return await Data<TEntity, TKey>.DeleteManyAsync(ids, set, ct);
    }

    public static Task<int> RemoveAll(CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAllAsync(ct);

    // Instance self-remove
    public Task<bool> Remove(CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(Id, ct);

}

// Convenience for string-keyed entities
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
