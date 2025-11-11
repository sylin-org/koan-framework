using System;

namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Immutable definition for a single vector metadata property.
/// </summary>
public sealed class VectorSchemaProperty
{
    public VectorSchemaProperty(
        string name,
        VectorSchemaPropertyType type,
        bool isRequired,
        bool isSearchable,
        bool isFilterable,
        bool isSortable,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(name));
        }

        Name = name;
        Type = type;
        IsRequired = isRequired;
        IsSearchable = isSearchable;
        IsFilterable = isFilterable;
        IsSortable = isSortable;
        Description = description;
    }

    public string Name { get; }
    public VectorSchemaPropertyType Type { get; }
    public bool IsRequired { get; }
    public bool IsSearchable { get; }
    public bool IsFilterable { get; }
    public bool IsSortable { get; }
    public string? Description { get; }
}
