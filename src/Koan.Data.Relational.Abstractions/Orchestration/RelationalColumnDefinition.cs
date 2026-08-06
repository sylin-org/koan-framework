namespace Koan.Data.Relational.Orchestration;

/// <summary>Describes one column requested by the provider-neutral schema owner.</summary>
public sealed record RelationalColumnDefinition(
    string Name,
    Type ClrType,
    bool Nullable,
    bool IsComputed = false,
    string? JsonPath = null,
    bool IsIndexed = false,
    RelationalStorageShape Shape = RelationalStorageShape.Scalar,
    bool IsIdentity = false,
    bool IsGenerated = false);
