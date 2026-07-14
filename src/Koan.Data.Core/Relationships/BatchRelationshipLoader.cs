using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Core.Hosting.App;

namespace Koan.Data.Core.Relationships
{
    /// <summary>
    /// Batch loader for parent and child relationships. Optimizes enrichment by grouping queries.
    /// </summary>
    public class BatchRelationshipLoader
    {
        public async Task<Dictionary<(string PropertyName, Type ParentType), Dictionary<object, object?>>> LoadParentsBatch<TEntity, TKey>(
            IReadOnlyList<TEntity> entities,
            IRelationshipMetadata metadata,
            CancellationToken ct = default)
            where TEntity : IEntity<TKey>
            where TKey : notnull
        {
            var result = new Dictionary<(string, Type), Dictionary<object, object?>>();
            var parentRels = metadata.GetParentRelationships(typeof(TEntity));
            foreach (var (propertyName, parentType) in parentRels)
            {
                var property = typeof(TEntity).GetProperty(propertyName);
                if (property is null)
                {
                    continue;
                }

                var parentIds = entities
                    .Select(e => property.GetValue(e))
                    .OfType<TKey>()
                    .Distinct()
                    .ToList();

                var parentDict = new Dictionary<object, object?>();
                if (parentIds.Count > 0)
                {
                    var dataType = typeof(Data<,>).MakeGenericType(parentType, typeof(TKey));
                    var method = dataType.GetMethod("Get", new[] { typeof(TKey), typeof(CancellationToken) });
                    if (method is null)
                    {
                        continue;
                    }

                    foreach (var id in parentIds)
                    {
                        var parameters = new object[] { id, ct };
                        if (method.Invoke(null, parameters) is not Task task)
                        {
                            continue;
                        }

                        await task;
                        var resultProp = task.GetType().GetProperty("Result");
                        var parent = resultProp?.GetValue(task);
                        parentDict[id] = parent;
                    }
                }

                result[(propertyName, parentType)] = parentDict;
            }
            return result;
        }

        public async Task<Dictionary<(string ReferenceProperty, Type ChildType), Dictionary<object, List<object>>>> LoadChildrenBatch<TEntity, TKey>(
            IReadOnlyList<TEntity> entities,
            IRelationshipMetadata metadata,
            CancellationToken ct = default)
            where TEntity : IEntity<TKey>
            where TKey : notnull
        {
            var result = new Dictionary<(string, Type), Dictionary<object, List<object>>>();
            var executor = AppHost.GetRequiredService<IRelationshipQueryExecutor>("batch relationship child loading");
            var childRels = metadata.GetChildRelationships(typeof(TEntity));
            foreach (var (referenceProperty, childType) in childRels)
            {
                var entityIds = entities.Select(e => e.Id).Where(id => id is not null).Cast<TKey>().Distinct().ToList();
                if (entityIds.Count == 0)
                {
                    continue;
                }

                var method = typeof(BatchRelationshipLoader).GetMethod(
                    nameof(LoadChildEdge),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .MakeGenericMethod(typeof(TEntity), childType, typeof(TKey));
                var childDict = await (Task<Dictionary<object, List<object>>>)method.Invoke(
                    null,
                    new object[] { executor, entityIds, referenceProperty, ct })!;

                result[(referenceProperty, childType)] = childDict;
            }
            return result;
        }

        private static async Task<Dictionary<object, List<object>>> LoadChildEdge<TParent, TChild, TKey>(
            IRelationshipQueryExecutor executor,
            IReadOnlyCollection<TKey> parentIds,
            string referenceProperty,
            CancellationToken ct)
            where TParent : class, IEntity<TKey>
            where TChild : class, IEntity<TKey>
            where TKey : notnull
        {
            var edge = await executor.LoadChildren<TParent, TChild, TKey>(
                parentIds, referenceProperty, ct: ct);
            return edge.ByParent.ToDictionary(
                pair => (object)pair.Key,
                pair => pair.Value.Cast<object>().ToList());
        }
    }
}
