using System;

namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Declares the metadata type that should be used to describe an entity's vector schema.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class VectorSchemaAttribute : Attribute
{
    public VectorSchemaAttribute(Type metadataType)
    {
        MetadataType = metadataType ?? throw new ArgumentNullException(nameof(metadataType));
    }

    public Type MetadataType { get; }

    /// <summary>
    /// Optional canonical name used when creating vector collections for this entity.
    /// Defaults to the entity type name when not supplied.
    /// </summary>
    public string? EntityName { get; set; }
}
