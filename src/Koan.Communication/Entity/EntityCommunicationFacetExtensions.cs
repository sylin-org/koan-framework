using Koan.Data.Abstractions;
using Koan.Data.Core.Selection;

namespace Koan.Data.Core.Model;

/// <summary>Adds pointwise Communication intents when the Communication module is referenced.</summary>
public static class EntityCommunicationFacetExtensions
{
    extension<TEntity>(TEntity entity) where TEntity : class, IEntity
    {
        /// <summary>Begins an Event occurrence operation for this Entity.</summary>
        public EntityEventsFacet<TEntity> Events => new(EntityCardinality.One(entity));

        /// <summary>Begins a Transport operation for this Entity snapshot.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.One(entity));
    }

    extension<TEntity>(IEnumerable<TEntity> entities) where TEntity : class, IEntity
    {
        /// <summary>Begins pointwise Event occurrence operations for this finite Entity source.</summary>
        public EntityEventsFacet<TEntity> Events => new(EntityCardinality.Many(entities));

        /// <summary>Begins a pointwise Transport operation for this finite Entity source.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.Many(entities));
    }

    extension<TEntity>(IAsyncEnumerable<TEntity> entities) where TEntity : class, IEntity
    {
        /// <summary>Begins pointwise Event occurrence operations for this lazy Entity stream.</summary>
        public EntityEventsFacet<TEntity> Events => new(EntityCardinality.Stream(entities));

        /// <summary>Begins a pointwise Transport operation for this lazy Entity stream.</summary>
        public EntityTransportFacet<TEntity> Transport => new(EntityCardinality.Stream(entities));
    }
}
