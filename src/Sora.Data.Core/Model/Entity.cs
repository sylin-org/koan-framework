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
        => Sora.Data.Core.Data<TEntity, TKey>.GetAsync(id, ct);

    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.All(ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.Query(query, ct);

    // Streaming (IAsyncEnumerable)
    public static IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.AllStream(batchSize, ct);
    public static IAsyncEnumerable<TEntity> QueryStream(string query, int? batchSize = null, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.QueryStream(query, batchSize, ct);

    // Basic paging helpers (materialized)
    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.FirstPage(size, ct);
    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.Page(page, size, ct);

    // Counts
    public static Task<int> Count(CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.CountAllAsync(ct);
    public static Task<int> Count(string query, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.CountAsync(query, ct);

    public static IBatchSet<TEntity, TKey> Batch() => Sora.Data.Core.Data<TEntity, TKey>.Batch();

    public static Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.UpsertManyAsync(models, ct);

    // Removal helpers
    public static Task<bool> Remove(TKey id, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.DeleteAsync(id, ct);

    public static Task<int> Remove(IEnumerable<TKey> ids, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.DeleteManyAsync(ids, ct);

    public static async Task<int> Remove(string query, CancellationToken ct = default)
    {
        var items = await Sora.Data.Core.Data<TEntity, TKey>.Query(query, ct);
        var ids = System.Linq.Enumerable.Select<TEntity, TKey>(items, e => e.Id);
        return await Sora.Data.Core.Data<TEntity, TKey>.DeleteManyAsync(ids, ct);
    }

    public static Task<int> RemoveAll(CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.DeleteAllAsync(ct);

    // Instance self-remove
    public Task<bool> Remove(CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.DeleteAsync(Id, ct);

}

// Convenience for string-keyed entities
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
