namespace Koan.Data.Abstractions;

/// <summary>Native scalar cardinality and neutral value receipt.</summary>
public sealed record SourceScalarResult(
    int RecordCount,
    int FieldCount,
    object? Value,
    string? ProviderTypeName = null,
    bool HasAdditionalResultChannels = false);
