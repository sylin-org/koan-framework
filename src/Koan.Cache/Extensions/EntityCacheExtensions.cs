using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Keys;
using Koan.Data.Abstractions;
using Koan.Data.Core;
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
    /// Removes this entity's cache entry from the registered cache store. Builds the SAME scoped key the read
    /// path cached under (gap B): the canonical <c>{TypeName}:{Partition}:{Id}</c> shape plus any managed
    /// equality-axis scope (e.g. the tenant discriminator), read from the ambient <c>EntityContext</c>. Call it
    /// under the same scope (tenant) the entity was cached under.
    /// </summary>
    public static async ValueTask<bool> Uncache<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // A default/unset id was never cached (the write path skips a default key — CachedRepository.IsDefaultKey);
        // mirror that here so Uncache stays an idempotent no-op rather than throwing on a null id.
        if (EqualityComparer<TKey>.Default.Equals(entity.Id, default!)) return false;
        var client = ResolveClient();
        var key = ScopedEntityCacheKey.For(typeof(TEntity), entity.Id, EntityContext.Current?.Partition);
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

    private static ICacheWriter ResolveClient()
        => Koan.Core.Hosting.App.AppHost.GetRequiredService<ICacheWriter>("entity cache eviction");
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
    /// Removes the cache entry for the entity with the given <paramref name="id"/>. Builds the SAME scoped key the
    /// read path cached under (gap B): <c>{TypeName}:{Partition}:{Id}</c> plus any managed equality-axis scope, read
    /// from the ambient <c>EntityContext</c>. Call it under the same scope (tenant) the entity was cached under.
    /// </summary>
    public ValueTask<bool> Flush(TKey id, CancellationToken ct = default)
    {
        // A default/unset id was never cached; no-op rather than throw (parity with Uncache + the write path).
        if (EqualityComparer<TKey>.Default.Equals(id, default!)) return new ValueTask<bool>(false);
        var key = ScopedEntityCacheKey.For(typeof(TEntity), id, EntityContext.Current?.Partition);
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
