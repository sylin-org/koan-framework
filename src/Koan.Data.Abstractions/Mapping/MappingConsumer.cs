namespace Koan.Data.Abstractions;

/// <summary>A semantic consumer of a compiled logical-to-physical binding.</summary>
public enum MappingConsumer
{
    Hydration,
    Write,
    Filter,
    Order,
    Patch,
    ConditionalWrite,
    Projection,
    Index
}
