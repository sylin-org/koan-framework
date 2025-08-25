namespace Sora.Data.Abstractions.Vector.Filtering;

public sealed record VectorFilterAnd(IReadOnlyList<VectorFilter> Operands) : VectorFilter
{
    public VectorFilterAnd(params VectorFilter[] ops) : this((IReadOnlyList<VectorFilter>)ops) { }
}