using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core.Relationships;

namespace Sora.Data.Core.Model;

// Domain-centric CRTP base with static conveniences, independent of data namespace
public abstract class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    [Identifier]
    [Key]
    public TKey Id { get; set; } = default!;

    // Static conveniences forward to the data facade without exposing its namespace in domain types
    public static Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => Data<TEntity, TKey>.GetAsync(id, ct);
    // Set-aware variants
    public static Task<TEntity?> Get(TKey id, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.GetAsync(id, set, ct);

    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Data<TEntity, TKey>.All(ct);
    public static Task<IReadOnlyList<TEntity>> All(string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.All(set, ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Data<TEntity, TKey>.Query(query, ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.Query(query, set, ct);

    // Streaming (IAsyncEnumerable)
    public static IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default)
        => Data<TEntity, TKey>.AllStream(batchSize, ct);
    public static IAsyncEnumerable<TEntity> QueryStream(string query, int? batchSize = null, CancellationToken ct = default)
        => Data<TEntity, TKey>.QueryStream(query, batchSize, ct);

    // Basic paging helpers (materialized)
    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
        => Data<TEntity, TKey>.FirstPage(size, ct);
    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
        => Data<TEntity, TKey>.Page(page, size, ct);

    // Counts
    public static Task<int> Count(CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(ct);
    public static Task<int> Count(string query, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAsync(query, ct);
    public static Task<int> CountAll(string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(set, ct);
    public static Task<int> Count(string query, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAsync(query, set, ct);

    public static IBatchSet<TEntity, TKey> Batch() => Data<TEntity, TKey>.Batch();

    public static Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => Data<TEntity, TKey>.UpsertManyAsync(models, ct);

    // Removal helpers
    public static Task<bool> Remove(TKey id, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(id, ct);

    public static Task<int> Remove(IEnumerable<TKey> ids, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteManyAsync(ids, ct);

    // Set-aware removal helpers
    public static Task<bool> Remove(TKey id, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(id, set, ct);

    public static Task<int> Remove(IEnumerable<TKey> ids, string set, CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteManyAsync(ids, set, ct);

    public static async Task<int> Remove(string query, CancellationToken ct = default)
    {
        var items = await Data<TEntity, TKey>.Query(query, ct);
        var ids = Enumerable.Select<TEntity, TKey>(items, e => e.Id);
        return await Data<TEntity, TKey>.DeleteManyAsync(ids, ct);
    }

    public static async Task<int> Remove(string query, string set, CancellationToken ct = default)
    {
        var items = await Data<TEntity, TKey>.Query(query, set, ct);
        var ids = Enumerable.Select<TEntity, TKey>(items, e => e.Id);
        return await Data<TEntity, TKey>.DeleteManyAsync(ids, set, ct);
    }

    public static Task<int> RemoveAll(CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAllAsync(ct);

    // Instance self-remove
    public Task<bool> Remove(CancellationToken ct = default)
        => Data<TEntity, TKey>.DeleteAsync(Id, ct);

    // Instance-based relationship navigation methods

    /// <summary>
    /// Gets the single parent entity for this instance. Only works when the entity has exactly one parent relationship.
    /// Throws InvalidOperationException if the entity has no parents or multiple parents.
    /// </summary>
    public async Task<object> GetParent(CancellationToken ct = default)
    {
        var relationshipService = GetRelationshipService();
        relationshipService.ValidateRelationshipCardinality(typeof(TEntity), "getparent");

        var parentRelationships = relationshipService.GetParentRelationships(typeof(TEntity));
        var (propertyName, parentType) = parentRelationships[0];

        var parentId = GetPropertyValue<TKey>(propertyName);
        if (parentId == null) return null!;

        var result = await LoadParentEntity(parentType, parentId, ct);
        return result ?? throw new InvalidOperationException($"Parent entity not found for {propertyName} = {parentId}");
    }

    /// <summary>
    /// Gets the parent entity of the specified type for this instance.
    /// Validates that exactly one relationship to TParent exists.
    /// </summary>
    public async Task<TParent?> GetParent<TParent>(CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
    {
        var relationshipService = GetRelationshipService();
        var parentRelationships = relationshipService.GetParentRelationships(typeof(TEntity))
            .Where(x => x.ParentType == typeof(TParent))
            .ToList();

        if (parentRelationships.Count == 0)
            throw new InvalidOperationException($"{typeof(TEntity).Name} has no parent relationship to {typeof(TParent).Name}");

        if (parentRelationships.Count > 1)
            throw new InvalidOperationException($"{typeof(TEntity).Name} has multiple parent relationships to {typeof(TParent).Name}. Use GetParent<TParent>(propertyName) instead");

        var (propertyName, _) = parentRelationships[0];
        var parentId = GetPropertyValue<TKey>(propertyName);

        if (parentId == null) return null;

        return await Data<TParent, TKey>.GetAsync(parentId, ct);
    }

    /// <summary>
    /// Gets the parent entity of the specified type using the specified property name.
    /// No validation - allows explicit access to specific parent relationships.
    /// </summary>
    public async Task<TParent?> GetParent<TParent>(string propertyName, CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
    {
        var parentId = GetPropertyValue<TKey>(propertyName);
        if (parentId == null) return null;

        return await Data<TParent, TKey>.GetAsync(parentId, ct);
    }

    /// <summary>
    /// Gets all parent entities for this instance as a dictionary.
    /// Key = property name, Value = parent entity or null.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetParents(CancellationToken ct = default)
    {
        var relationshipService = GetRelationshipService();
        var parentRelationships = relationshipService.GetParentRelationships(typeof(TEntity));
        var parents = new Dictionary<string, object?>();

        foreach (var (propertyName, parentType) in parentRelationships)
        {
            var parentId = GetPropertyValue<TKey>(propertyName);
            if (parentId != null)
            {
                var parent = await LoadParentEntity(parentType, parentId, ct);
                parents[propertyName] = parent;
            }
            else
            {
                parents[propertyName] = null;
            }
        }

        return parents;
    }

    /// <summary>
    /// Gets all children for this entity when it has exactly one child type.
    /// Throws InvalidOperationException if the entity has no children or multiple child types.
    /// </summary>
    public async Task<IReadOnlyList<object>> GetChildren(CancellationToken ct = default)
    {
        var relationshipService = GetRelationshipService();
        relationshipService.ValidateRelationshipCardinality(typeof(TEntity), "getchildren");

        var childTypes = relationshipService.GetAllChildTypes(typeof(TEntity));
        var childType = childTypes[0];

        return await LoadChildEntities(childType, ct);
    }

    /// <summary>
    /// Gets all children of the specified type for this entity.
    /// </summary>
    public async Task<IReadOnlyList<TChild>> GetChildren<TChild>(CancellationToken ct = default)
        where TChild : class, IEntity<TKey>
    {
        var relationshipService = GetRelationshipService();
        var childRelationships = relationshipService.GetChildRelationships(typeof(TEntity))
            .Where(x => x.ChildType == typeof(TChild))
            .ToList();

        if (childRelationships.Count == 0)
            return new List<TChild>().AsReadOnly();

        var allChildren = new List<TChild>();
        foreach (var (referenceProperty, _) in childRelationships)
        {
            var children = await LoadChildrenByProperty<TChild>(referenceProperty, ct);
            allChildren.AddRange(children);
        }

        return allChildren.AsReadOnly();
    }

    /// <summary>
    /// Gets children of the specified type using the specified reference property.
    /// </summary>
    public async Task<IReadOnlyList<TChild>> GetChildren<TChild>(string referenceProperty, CancellationToken ct = default)
        where TChild : class, IEntity<TKey>
    {
        return await LoadChildrenByProperty<TChild>(referenceProperty, ct);
    }

    /// <summary>
    /// Gets the full relationship graph for this entity, including all parents and children.
    /// Returns a RelationshipGraph with selective enrichment - only this entity is enriched.
    /// </summary>
    public async Task<RelationshipGraph<TEntity>> GetRelatives(CancellationToken ct = default)
    {
        var relationshipGraph = new RelationshipGraph<TEntity>
        {
            Entity = (TEntity)(object)this
        };

        // Load all parents
        relationshipGraph.Parents = await GetParents(ct);

        // Load all children grouped by class name
        var relationshipService = GetRelationshipService();
        var childRelationships = relationshipService.GetChildRelationships(typeof(TEntity));

        foreach (var (referenceProperty, childType) in childRelationships)
        {
            var children = await LoadChildEntitiesByProperty(childType, referenceProperty, ct);

            var childTypeName = childType.Name;
            if (!relationshipGraph.Children.ContainsKey(childTypeName))
            {
                relationshipGraph.Children[childTypeName] = new Dictionary<string, IReadOnlyList<object>>();
            }

            relationshipGraph.Children[childTypeName][referenceProperty] = children;
        }

        return relationshipGraph;
    }

    // Helper methods for relationship loading

    private TValue? GetPropertyValue<TValue>(string propertyName)
    {
        var property = typeof(TEntity).GetProperty(propertyName);
        if (property == null) return default(TValue);

        var value = property.GetValue(this);
        if (value == null) return default(TValue);

        try
        {
            return (TValue)value;
        }
        catch
        {
            return default(TValue);
        }
    }

    private async Task<object?> LoadParentEntity(Type parentType, TKey parentId, CancellationToken ct)
    {
        // Use reflection to call Data<TParent, TKey>.GetAsync
        var dataType = typeof(Data<,>).MakeGenericType(parentType, typeof(TKey));
        var method = dataType.GetMethod("GetAsync", new[] { typeof(TKey), typeof(CancellationToken) });

        if (method == null) return null;

        var task = (Task)method.Invoke(null, new object[] { parentId, ct })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    private async Task<IReadOnlyList<object>> LoadChildEntities(Type childType, CancellationToken ct)
    {
        // Use reflection to call Data<TChild, TKey>.Query to find children referencing this entity
        var dataType = typeof(Data<,>).MakeGenericType(childType, typeof(TKey));
        var allMethod = dataType.GetMethod("All", new[] { typeof(CancellationToken) });

        if (allMethod == null) return new List<object>().AsReadOnly();

        var task = (Task)allMethod.Invoke(null, new object[] { ct })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result");
        var allResults = (System.Collections.IEnumerable?)resultProperty?.GetValue(task);

        if (allResults == null) return new List<object>().AsReadOnly();

        // Filter children that reference this entity
        var children = new List<object>();
        var relationshipService = GetRelationshipService();
        var childRelationships = relationshipService.GetChildRelationships(typeof(TEntity))
            .Where(x => x.ChildType == childType);

        foreach (var item in allResults)
        {
            foreach (var (referenceProperty, _) in childRelationships)
            {
                var property = childType.GetProperty(referenceProperty);
                if (property != null)
                {
                    var referenceValue = property.GetValue(item);
                    if (Equals(referenceValue, Id))
                    {
                        children.Add(item);
                        break; // Found a match, no need to check other reference properties for this item
                    }
                }
            }
        }

        return children.AsReadOnly();
    }

    private async Task<IReadOnlyList<TChild>> LoadChildrenByProperty<TChild>(string referenceProperty, CancellationToken ct)
        where TChild : class, IEntity<TKey>
    {
        // For now, load all children and filter in memory
        // This could be optimized with query support in the future
        var allChildren = await Data<TChild, TKey>.All(ct);

        var matchingChildren = new List<TChild>();
        var property = typeof(TChild).GetProperty(referenceProperty);

        if (property != null)
        {
            foreach (var child in allChildren)
            {
                var referenceValue = property.GetValue(child);
                if (Equals(referenceValue, Id))
                {
                    matchingChildren.Add(child);
                }
            }
        }

        return matchingChildren.AsReadOnly();
    }

    private async Task<IReadOnlyList<object>> LoadChildEntitiesByProperty(Type childType, string referenceProperty, CancellationToken ct)
    {
        // Use reflection to call Data<TChild, TKey>.All() and filter by reference property
        var dataType = typeof(Data<,>).MakeGenericType(childType, typeof(TKey));
        var allMethod = dataType.GetMethod("All", new[] { typeof(CancellationToken) });

        if (allMethod == null) return new List<object>().AsReadOnly();

        var task = (Task)allMethod.Invoke(null, new object[] { ct })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result");
        var allResults = (System.Collections.IEnumerable?)resultProperty?.GetValue(task);

        if (allResults == null) return new List<object>().AsReadOnly();

        var children = new List<object>();
        var property = childType.GetProperty(referenceProperty);

        if (property != null)
        {
            foreach (var item in allResults)
            {
                var referenceValue = property.GetValue(item);
                if (Equals(referenceValue, Id))
                {
                    children.Add(item);
                }
            }
        }

        return children.AsReadOnly();
    }

    private IRelationshipMetadata GetRelationshipService()
    {
        // For now, create a new instance. This could be optimized with DI or caching
        return new RelationshipMetadataService();
    }

}

// Convenience for string-keyed entities
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
