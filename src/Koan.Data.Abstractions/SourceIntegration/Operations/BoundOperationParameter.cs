namespace Koan.Data.Abstractions;

public sealed record BoundOperationParameter(string Name, Type ValueType, object? Value);
