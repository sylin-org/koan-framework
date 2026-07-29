namespace Koan.Data.Abstractions;

/// <summary>The mutation shape for which mapped values are being produced.</summary>
public enum MappingWriteOperation
{
    Insert,
    Update,
    Patch,
    ConditionalWrite
}
