namespace Koan.Data.Abstractions;

/// <summary>One present physical value produced or consumed by a compiled map.</summary>
public sealed record MappedValue(
    string BindingId,
    PhysicalPath Path,
    MappingValueShape Shape,
    object? Value);
