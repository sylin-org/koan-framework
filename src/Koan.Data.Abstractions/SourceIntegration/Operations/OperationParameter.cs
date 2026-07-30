namespace Koan.Data.Abstractions;

public sealed record OperationParameter(string Name, Type ValueType, bool Required = true);
