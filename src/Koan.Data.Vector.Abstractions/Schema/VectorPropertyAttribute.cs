using System;

namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Supplies schema hints for vector metadata projection.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class VectorPropertyAttribute : Attribute
{
    public VectorPropertyAttribute(VectorSchemaPropertyType type)
    {
        Type = type;
    }

    /// <summary>
    /// Optional explicit schema property name. Falls back to camelCase of the CLR property name when omitted.
    /// </summary>
    public string? Name { get; set; }

    public VectorSchemaPropertyType Type { get; }
    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool Filterable { get; set; }
    public bool Sortable { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// When true, skips this property from the generated schema.
    /// </summary>
    public bool Ignore { get; set; }
}
