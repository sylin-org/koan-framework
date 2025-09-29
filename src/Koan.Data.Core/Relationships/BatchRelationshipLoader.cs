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
                    var method = dataType.GetMethod("GetAsync", new[] { typeof(TKey), typeof(CancellationToken) });
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
                var entityIds = entities.Select(e => e.Id).Where(id => id is not null).Cast<TKey>().Distinct().ToList();
                if (entityIds.Count == 0)
                {
                    continue;
                }

                var childDict = new Dictionary<object, List<object>>();
                var dataType = typeof(Data<,>).MakeGenericType(childType, typeof(TKey));
                var allMethod = dataType.GetMethod("All", new[] { typeof(CancellationToken) });
                if (allMethod is null)
                {
                    continue;
                }

                var allTask = allMethod.Invoke(null, new object[] { ct }) as Task;
                if (allTask is null)
                {
                    continue;
                }

                await allTask.ConfigureAwait(false);
                var resultProp = allTask.GetType().GetProperty("Result");
                var allChildren = resultProp?.GetValue(allTask) as System.Collections.IEnumerable;
                if (allChildren is null)
                {
                    continue;
                }

                var referencePropertyInfo = childType.GetProperty(referenceProperty);
                if (referencePropertyInfo is null)
                {
                    continue;
                }

                foreach (var id in entityIds)
                {
                    var matches = new List<object>();
                    foreach (var child in allChildren)
                    {
                        var value = referencePropertyInfo.GetValue(child);
                        if (Equals(value, id))
                        {
                            matches.Add(child);
                        }
                    }

                    childDict[id] = matches;
                }

                result[(referenceProperty, childType)] = childDict;
            }
            return result;
        }
    }
}
