using Koan.Data.Abstractions;
using Koan.Data.Core.Selection;

namespace Koan.Communication;

/// <summary>Adds pointwise Transport intent to one Entity, a finite Entity set, or an async Entity stream.</summary>
public static class EntityTransportFacetExtensions
{
    extension<TEntity>(TEntity entity) where TEntity : class, IEntity
    {
        /// <summary>Begins a Transport operation for this Entity snapshot.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.One(entity));
    }

    extension<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity
    {
        /// <summary>Begins a pointwise Transport operation for this finite Entity source.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.Many(entities));
    }

    extension<TEntity>(IAsyncEnumerable<TEntity> entities) where TEntity : class, IEntity
    {
        /// <summary>Begins a pointwise Transport operation for this lazy Entity stream.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.Stream(entities));
    }
}
