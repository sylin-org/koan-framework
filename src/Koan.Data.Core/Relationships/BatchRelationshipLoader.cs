using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

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
                var parentIds = entities
                    .Select(e => typeof(TEntity).GetProperty(propertyName)?.GetValue(e))
                    .Where(id => id != null)
                    .Distinct()
                    .ToList();
                var parentDict = new Dictionary<object, object?>();
                if (parentIds.Count > 0)
                {
                    var dataType = typeof(Data<,>).MakeGenericType(parentType, typeof(TKey));
                    var method = dataType.GetMethod("GetAsync", new[] { typeof(TKey), typeof(CancellationToken) });
                    foreach (var id in parentIds)
                    {
                        var task = (Task)method.Invoke(null, new object[] { id, ct });
                        await task.ConfigureAwait(false);
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
            var childRels = metadata.GetChildRelationships(typeof(TEntity));
            foreach (var (referenceProperty, childType) in childRels)
            {
                var entityIds = entities.Select(e => e.Id).Distinct().ToList();
                var childDict = new Dictionary<object, List<object>>();
                var dataType = typeof(Data<,>).MakeGenericType(childType, typeof(TKey));
                var allMethod = dataType.GetMethod("All", new[] { typeof(CancellationToken) });
                var task = (Task)allMethod.Invoke(null, new object[] { ct });
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var allChildren = (System.Collections.IEnumerable?)resultProp?.GetValue(task);
                if (allChildren != null)
                {
                    foreach (var id in entityIds)
                    {
                        var matches = new List<object>();
                        foreach (var child in allChildren)
                        {
                            var prop = childType.GetProperty(referenceProperty);
                            if (prop != null && Equals(prop.GetValue(child), id))
                            {
                                matches.Add(child);
                            }
                        }
                        childDict[id] = matches;
                    }
                }
                result[(referenceProperty, childType)] = childDict;
            }
            return result;
        }
    }
}
