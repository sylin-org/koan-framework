using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Sora.Data.Abstractions;
using Sora.Data.Core.Model;
using Sora.Data.Core.Relationships;

namespace Sora.Data.Core.Extensions;

/// <summary>
/// Extension methods for batch relationship loading and streaming operations.
/// Provides clean syntax for enriching entities with their relationships.
/// </summary>
public static class RelationshipExtensions
{
    /// <summary>
    /// Enriches a collection of entities with their relationships.
    /// Processes all entities in the collection - pure transformation, no batching concerns.
    /// </summary>
    public static async Task<IReadOnlyList<RelationshipGraph<TEntity>>> Relatives<TEntity, TKey>(
        this IEnumerable<TEntity> entities,
        CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull
    {
        var entityList = entities.ToList();
        if (entityList.Count == 0) return new List<RelationshipGraph<TEntity>>().AsReadOnly();

        var enrichedResults = new List<RelationshipGraph<TEntity>>();
        foreach (var entity in entityList)
        {
            var enriched = await entity.GetRelatives(ct);
            enrichedResults.Add(enriched);
        }

        return enrichedResults.AsReadOnly();
    }

    /// <summary>
    /// Enriches a stream of entities with their relationships.
    /// Processes each entity as it arrives in the stream - pure transformation, no batching.
    /// </summary>
    public static async IAsyncEnumerable<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this IAsyncEnumerable<TEntity> entities,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull
    {
        await foreach (var entity in entities.WithCancellation(ct))
        {
            var enriched = await entity.GetRelatives(ct);
            yield return enriched;
        }
    }

    /// <summary>
    /// Enriches a single entity with its relationships.
    /// Convenience method for individual entity enrichment.
    /// </summary>
    public static async Task<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this TEntity entity,
        CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull
    {
        return await entity.GetRelatives(ct);
    }

}