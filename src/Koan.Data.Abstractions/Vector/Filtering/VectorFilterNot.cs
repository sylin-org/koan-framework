namespace Koan.Data.Abstractions.Vector.Filtering;

public sealed record VectorFilterNot(VectorFilter Operand) : VectorFilter;