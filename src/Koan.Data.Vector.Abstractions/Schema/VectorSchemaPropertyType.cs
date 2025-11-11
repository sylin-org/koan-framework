namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Supported primitive shapes for vector metadata properties.
/// Keeps cross-adapter schema generation consistent.
/// </summary>
public enum VectorSchemaPropertyType
{
    Text,
    TextArray,
    Int,
    Long,
    Number,
    Boolean,
    DateTime
}
