using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Cache.Extensions;

/// <summary>
/// Cache extension methods for <see cref="Entity{TEntity, TKey}"/> types.
/// These extensions are available when Koan.Cache is referenced, bridging the entity
/// and cache modules without coupling Koan.Data.Core to any cache implementation.
/// </summary>
public static class EntityCacheExtensions
{
    /// <summary>
    /// Removes this entity's cache entry from the registered cache store.
    /// Uses the convention-based key format: <c>{TypeName}:{Id}</c>.
    /// </summary>
    public static async ValueTask<bool> Uncache<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var client = ResolveClient();
        var key = BuildEntityKey<TEntity, TKey>(entity.Id);
        return await client.Remove(key, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a cache handle scoped to the given entity type, providing static-style
    /// operations such as <see cref="EntityCacheHandle{TEntity, TKey}.Flush"/>.
    /// Usage: <c>var cache = EntityCacheExtensions.Cache&lt;Product, Guid&gt;();</c>
    /// </summary>
    public static EntityCacheHandle<TEntity, TKey> Cache<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new(ResolveClient());

    private static CacheKey BuildEntityKey<TEntity, TKey>(TKey id)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new($"{CacheKey.EntityTypeName(typeof(TEntity))}:{id}");

    private static ICacheWriter ResolveClient()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "AppHost is not initialized. Ensure the application host is started before using entity cache extensions.");
        return (ICacheWriter)(provider.GetService(typeof(ICacheWriter))
            ?? throw new InvalidOperationException(
                "ICacheWriter is not registered. Ensure AddKoanCache() has been called."));
    }
}

/// <summary>
/// A typed handle providing entity-scoped cache operations for <typeparamref name="TEntity"/>.
/// Obtained via <see cref="EntityCacheExtensions.Cache{TEntity, TKey}"/>.
/// </summary>
public readonly struct EntityCacheHandle<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly ICacheWriter _writer;

    internal EntityCacheHandle(ICacheWriter writer)
    {
        _writer = writer;
    }

    /// <summary>
    /// Removes the cache entry for the entity with the given <paramref name="id"/>.
    /// </summary>
    public ValueTask<bool> Flush(TKey id, CancellationToken ct = default)
    {
        var key = new CacheKey($"{CacheKey.EntityTypeName(typeof(TEntity))}:{id}");
        return _writer.Remove(key, ct);
    }

    /// <summary>
    /// Flushes all cache entries tagged with the entity type name.
    /// Requires an <see cref="ICacheClient"/> registration (full client with tag support).
    /// </summary>
    public async ValueTask<long> FlushAll(CancellationToken ct = default)
    {
        if (_writer is ICacheClient client)
        {
            return await client.FlushTags([CacheKey.EntityTypeName(typeof(TEntity))], ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "FlushAll requires ICacheClient with tag support. Ensure AddKoanCache() has been called.");
    }
}
