using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Data.Core.Extensions;

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

        var metadata = entityList[0].GetRelationshipService();
        var batchLoader = new BatchRelationshipLoader();

        // Batch load parents
        var parentMap = await batchLoader.LoadParentsBatch<TEntity, TKey>(entityList, metadata, ct);
        // Batch load children
        var childMap = await batchLoader.LoadChildrenBatch<TEntity, TKey>(entityList, metadata, ct);

        var results = new List<RelationshipGraph<TEntity>>();
        foreach (var entity in entityList)
        {
            var graph = new RelationshipGraph<TEntity>
            {
                Entity = entity,
                Parents = new Dictionary<string, object?>(),
                Children = new Dictionary<string, Dictionary<string, IReadOnlyList<object>>>()
            };
            foreach (var ((propertyName, parentType), parentDict) in parentMap)
            {
                var id = typeof(TEntity).GetProperty(propertyName)?.GetValue(entity);
                if (id != null && parentDict.TryGetValue(id, out var parent))
                {
                    graph.Parents[propertyName] = parent;
                }
            }
            foreach (var ((referenceProperty, childType), childDict) in childMap)
            {
                var id = entity.Id;
                if (childDict.TryGetValue(id, out var children))
                {
                    var typeName = childType.Name;
                    if (!graph.Children.ContainsKey(typeName))
                        graph.Children[typeName] = new Dictionary<string, IReadOnlyList<object>>();
                    graph.Children[typeName][referenceProperty] = children;
                }
            }
            results.Add(graph);
        }
        return results.AsReadOnly();
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
        var batch = new List<TEntity>();
        const int batchSize = 100;
        await foreach (var entity in entities.WithCancellation(ct))
        {
            batch.Add(entity);
            if (batch.Count >= batchSize)
            {
                var enrichedBatch = await RelationshipExtensions.Relatives<TEntity, TKey>(batch, ct);
                foreach (var enriched in enrichedBatch)
                    yield return enriched;
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            var enrichedBatch = await RelationshipExtensions.Relatives<TEntity, TKey>(batch, ct);
            foreach (var enriched in enrichedBatch)
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