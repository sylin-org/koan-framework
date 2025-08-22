namespace Sora.Data.Abstractions.Vector.Filtering;

public enum VectorFilterOperator { Eq, Ne, Gt, Gte, Lt, Lte, Like, Contains, In, Between }

public sealed record VectorFilterAnd(IReadOnlyList<VectorFilter> Operands) : VectorFilter
{
    public VectorFilterAnd(params VectorFilter[] ops) : this((IReadOnlyList<VectorFilter>)ops) { }
}

public sealed record VectorFilterOr(IReadOnlyList<VectorFilter> Operands) : VectorFilter
{
    public VectorFilterOr(params VectorFilter[] ops) : this((IReadOnlyList<VectorFilter>)ops) { }
}

public sealed record VectorFilterNot(VectorFilter Operand) : VectorFilter;

public sealed record VectorFilterCompare(IReadOnlyList<string> Path, VectorFilterOperator Operator, object? Value) : VectorFilter;
