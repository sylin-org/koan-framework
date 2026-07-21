using System.Reflection;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Data.Core.Relationships;

/// <summary>
/// Loads direct relationship graphs for a lazy Entity source while preserving source order and
/// delegating every child edge to the bounded relationship executor.
/// </summary>
internal sealed class RelationshipGraphLoader(
    IRelationshipMetadata metadata,
    IRelationshipQueryExecutor executor)
{
    private static readonly MethodInfo LoadParentEdgeMethod = GetGenericMethod(nameof(LoadParentEdge));
    private static readonly MethodInfo LoadChildEdgeMethod = GetGenericMethod(nameof(LoadChildEdge));

    public async IAsyncEnumerable<RelationshipGraph<TEntity>> Load<TEntity, TKey>(
        IAsyncEnumerable<TEntity> entities,
        RelationshipQueryPolicy? policy = null,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entities);
        policy ??= RelationshipQueryPolicy.Strict;
        policy.Validate();

        var batch = new List<TEntity>(Infrastructure.Constants.Defaults.RelationshipBatchSize);
        await foreach (var entity in entities.WithCancellation(ct).ConfigureAwait(false))
        {
            batch.Add(entity);
            if (batch.Count < Infrastructure.Constants.Defaults.RelationshipBatchSize)
            {
                continue;
            }

            foreach (var graph in await LoadBatch<TEntity, TKey>(batch, policy, ct).ConfigureAwait(false))
            {
                yield return graph;
            }

            batch.Clear();
        }

        if (batch.Count == 0)
        {
            yield break;
        }

        foreach (var graph in await LoadBatch<TEntity, TKey>(batch, policy, ct).ConfigureAwait(false))
        {
            yield return graph;
        }
    }

    private async Task<IReadOnlyList<RelationshipGraph<TEntity>>> LoadBatch<TEntity, TKey>(
        IReadOnlyList<TEntity> entities,
        RelationshipQueryPolicy policy,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var graphs = entities.Select(static entity => new RelationshipGraph<TEntity>
        {
            Entity = entity,
            Parents = new Dictionary<string, object?>(),
            Children = new Dictionary<string, Dictionary<string, IReadOnlyList<object>>>()
        }).ToArray();

        foreach (var (propertyName, parentType) in metadata.GetParentRelationships(typeof(TEntity)))
        {
            var property = GetReadableProperty(typeof(TEntity), propertyName);
            var parentIds = entities
                .Select(entity => ReadKey<TEntity, TKey>(entity, property))
                .Where(static key => key.HasValue)
                .Select(static key => key.Value)
                .Distinct()
                .ToArray();
            var parents = await InvokeParentEdge<TKey>(parentType, parentIds, ct).ConfigureAwait(false);

            for (var index = 0; index < entities.Count; index++)
            {
                var key = ReadKey<TEntity, TKey>(entities[index], property);
                graphs[index].Parents[propertyName] = key.HasValue && parents.TryGetValue(key.Value, out var parent)
                    ? parent
                    : null;
            }
        }

        var entityIds = entities.Select(static entity => entity.Id).Distinct().ToArray();
        foreach (var (referenceProperty, childType) in metadata.GetChildRelationships(typeof(TEntity)))
        {
            var children = await InvokeChildEdge<TEntity, TKey>(
                childType,
                entityIds,
                referenceProperty,
                policy,
                ct).ConfigureAwait(false);
            var childTypeName = childType.Name;

            for (var index = 0; index < entities.Count; index++)
            {
                var graph = graphs[index];
                if (!graph.Children.TryGetValue(childTypeName, out var edges))
                {
                    edges = new Dictionary<string, IReadOnlyList<object>>();
                    graph.Children[childTypeName] = edges;
                }

                edges[referenceProperty] = children.TryGetValue(entities[index].Id, out var rows)
                    ? rows
                    : Array.Empty<object>();
            }
        }

        return graphs;
    }

    private async Task<IReadOnlyDictionary<TKey, object?>> InvokeParentEdge<TKey>(
        Type parentType,
        IReadOnlyCollection<TKey> parentIds,
        CancellationToken ct)
        where TKey : notnull
    {
        EnsureKeyType(parentType, typeof(TKey));
        var task = (Task<IReadOnlyDictionary<TKey, object?>>)LoadParentEdgeMethod
            .MakeGenericMethod(parentType, typeof(TKey))
            .Invoke(this, [parentIds, ct])!;
        return await task.ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<TKey, IReadOnlyList<object>>> InvokeChildEdge<TEntity, TKey>(
        Type childType,
        IReadOnlyCollection<TKey> parentIds,
        string referenceProperty,
        RelationshipQueryPolicy policy,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        EnsureKeyType(childType, typeof(TKey));
        var task = (Task<IReadOnlyDictionary<TKey, IReadOnlyList<object>>>)LoadChildEdgeMethod
            .MakeGenericMethod(typeof(TEntity), childType, typeof(TKey))
            .Invoke(this, [parentIds, referenceProperty, policy, ct])!;
        return await task.ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<TKey, object?>> LoadParentEdge<TParent, TKey>(
        IReadOnlyCollection<TKey> parentIds,
        CancellationToken ct)
        where TParent : class, IEntity<TKey>
        where TKey : notnull
    {
        if (parentIds.Count == 0)
        {
            return new Dictionary<TKey, object?>();
        }

        var parents = await Data<TParent, TKey>.GetMany(parentIds, ct).ConfigureAwait(false);
        return parents
            .Where(static parent => parent is not null)
            .Cast<TParent>()
            .ToDictionary(static parent => parent.Id, static parent => (object?)parent);
    }

    private async Task<IReadOnlyDictionary<TKey, IReadOnlyList<object>>> LoadChildEdge<TParent, TChild, TKey>(
        IReadOnlyCollection<TKey> parentIds,
        string referenceProperty,
        RelationshipQueryPolicy policy,
        CancellationToken ct)
        where TParent : class, IEntity<TKey>
        where TChild : class, IEntity<TKey>
        where TKey : notnull
    {
        var result = await executor.LoadChildren<TParent, TChild, TKey>(
            parentIds,
            referenceProperty,
            policy: policy,
            ct: ct).ConfigureAwait(false);
        return result.ByParent.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<object>)pair.Value.Cast<object>().ToArray());
    }

    private static OptionalKey<TKey> ReadKey<TEntity, TKey>(TEntity entity, PropertyInfo property)
        where TEntity : class
        where TKey : notnull
    {
        var value = property.GetValue(entity);
        if (value is null)
        {
            return default;
        }

        return value is TKey key
            ? new OptionalKey<TKey>(key)
            : throw new InvalidOperationException(
                $"Relationship property '{typeof(TEntity).Name}.{property.Name}' has key type " +
                $"'{property.PropertyType.Name}', but {typeof(TEntity).Name} uses '{typeof(TKey).Name}'. " +
                "Use the same key type for both sides of a Koan relationship.");
    }

    private static PropertyInfo GetReadableProperty(Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Relationship property '{entityType.Name}.{propertyName}' was not found.");
        return property.CanRead
            ? property
            : throw new InvalidOperationException(
                $"Relationship property '{entityType.Name}.{propertyName}' is not readable.");
    }

    private static MethodInfo GetGenericMethod(string name)
        => typeof(RelationshipGraphLoader).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingMethodException(typeof(RelationshipGraphLoader).FullName, name);

    private static void EnsureKeyType(Type relatedType, Type keyType)
    {
        var matches = relatedType.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IEntity<>)
            && contract.GetGenericArguments()[0] == keyType);
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Relationship entity '{relatedType.Name}' does not use the source key type '{keyType.Name}'. " +
                "Koan direct relationships currently require the same key type on both sides.");
        }
    }

    private readonly record struct OptionalKey<TKey>(TKey Value)
        where TKey : notnull
    {
        public bool HasValue { get; } = true;
    }
}
