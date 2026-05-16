namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Controls how an entity's properties are mapped into a relational store.
/// Defaults to <see cref="RelationalStorageShape.Json"/> (entity stored as a JSON column)
/// when not specified.
/// </summary>
/// <remarks>
/// Use <see cref="RelationalStorageShape.PhysicalColumns"/> for entities that require
/// standard column-per-property mapping (e.g., for JOIN queries or direct SQL access).
/// Use <see cref="RelationalStorageShape.ComputedProjections"/> when you need a relational
/// view over a JSON-stored entity.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RelationalStorageAttribute : Attribute
{
    /// <summary>The relational mapping shape for this entity. Defaults to <see cref="RelationalStorageShape.Json"/>.</summary>
    public RelationalStorageShape Shape { get; init; } = RelationalStorageShape.Json;
}
