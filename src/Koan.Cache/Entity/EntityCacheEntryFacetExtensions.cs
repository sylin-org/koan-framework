using Koan.Data.Abstractions;
using Koan.Data.Core.Selection;

namespace Koan.Data.Core.Model;

/// <summary>Adds pointwise cache-entry intent when the Cache module is referenced.</summary>
public static class EntityCacheEntryFacetExtensions
{
    extension<TEntity, TKey>(Entity<TEntity, TKey> entity)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        /// <summary>Begins a cache-entry operation for this Entity.</summary>
        public EntityCacheEntryFacet<TEntity, TKey> Cache => new(EntityCardinality.One(entity));
    }

    extension<TEntity, TKey>(IEnumerable<Entity<TEntity, TKey>> entities)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        /// <summary>Begins pointwise cache-entry operations for this finite Entity source.</summary>
        public EntityCacheEntryFacet<TEntity, TKey> Cache => new(EntityCardinality.Many(entities));
    }

    extension<TEntity, TKey>(IAsyncEnumerable<Entity<TEntity, TKey>> entities)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        /// <summary>Begins pointwise cache-entry operations for this lazy Entity stream.</summary>
        public EntityCacheEntryFacet<TEntity, TKey> Cache => new(EntityCardinality.Stream(entities));
    }
}
