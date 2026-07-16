using Koan.Cache.Entity;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Model;

/// <summary>A terminal-only Entity cache-entry facet.</summary>
public readonly struct EntityCacheEntryFacet<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const string EvictionOperation = "entity cache-entry eviction";
    private readonly IAsyncEnumerable<Entity<TEntity, TKey>> _source;

    internal EntityCacheEntryFacet(IAsyncEnumerable<Entity<TEntity, TKey>> source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Removes every addressed Entity entry sequentially, preserving source order and returning a fixed-size outcome.
    /// Use this after an out-of-band write; ordinary Entity Save/Delete operations maintain cache state automatically.
    /// </summary>
    public Task<global::Koan.Cache.EntityCacheEviction> Evict(CancellationToken ct = default)
        => AppHost.GetRequiredService<EntityCacheEvictionCoordinator>(EvictionOperation)
            .Evict<TEntity, TKey>(_source, ct);
}
