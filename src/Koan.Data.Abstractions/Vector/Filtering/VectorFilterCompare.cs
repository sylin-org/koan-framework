namespace Koan.Data.Abstractions.Vector.Filtering;

public sealed record VectorFilterCompare(IReadOnlyList<string> Path, VectorFilterOperator Operator, object? Value) : VectorFilter;