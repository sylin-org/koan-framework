using System.Collections.Generic;

namespace Koan.Data.Core.Model;

/// <summary>
/// Enhanced response format that wraps an entity with its resolved parent and child relationships.
/// Used for selective enrichment where only requested entities receive the RelationshipGraph format.
/// Parents and children remain as raw entities without recursive enrichment.
/// </summary>
/// <typeparam name="TEntity">The type of the primary entity being enriched</typeparam>
public class RelationshipGraph<TEntity>
{
    /// <summary>
    /// The enriched entity being requested. This is the primary entity that was explicitly requested for enrichment.
    /// </summary>
    public TEntity Entity { get; set; } = default!;

    /// <summary>
    /// Raw parent entities (no recursive enrichment).
    /// Key = property name (e.g., "CustomerId"), Value = raw parent entity (e.g., Customer object).
    /// Only populated for properties that have [Parent(...)] attributes.
    /// </summary>
    public Dictionary<string, object?> Parents { get; set; } = new();

    /// <summary>
    /// Raw child entities (no recursive enrichment).
    /// Structure: ChildClassName -> ReferenceProperty -> Raw entities[]
    /// Example: { "OrderItem": { "OrderId": [item1, item2] }, "Review": { "OrderId": [review1] } }
    /// Organized by child class name for clear grouping, then by reference property for multiple relationships.
    /// </summary>
    public Dictionary<string, Dictionary<string, IReadOnlyList<object>>> Children { get; set; } = new();
}