namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Specifies how an entity's data is physically laid out in a relational store.
/// </summary>
public enum RelationalStorageShape
{
    /// <summary>
    /// The entire entity is serialised to a single JSON column (default).
    /// Schema-free; evolves without migrations. Recommended for most entities.
    /// </summary>
    Json,

    /// <summary>
    /// A computed/generated column or view projects selected JSON fields into queryable
    /// relational columns. The source of truth remains the JSON column.
    /// Use when you need SQL predicates on specific fields without full column expansion.
    /// </summary>
    ComputedProjections,

    /// <summary>
    /// Each property is mapped to its own relational column (traditional ORM-style).
    /// Requires schema migrations when the entity changes.
    /// Use for entities that need direct SQL joins, reporting queries, or third-party tools.
    /// </summary>
    PhysicalColumns
}