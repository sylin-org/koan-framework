namespace Koan.Data.Abstractions.Sources;

/// <summary>The proven effect of an operation at the source-policy boundary.</summary>
public enum DataOperationEffect
{
    Unknown,
    Read,
    Write,
    SchemaOrAdmin
}
