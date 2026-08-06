namespace Koan.Data.Relational.Mapping;

/// <summary>A relational physical binding paired with its already-encoded provider value.</summary>
public sealed record RelationalValue(RelationalPathBinding Binding, object? Value);
