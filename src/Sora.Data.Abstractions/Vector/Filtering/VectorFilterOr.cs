namespace Sora.Data.Abstractions.Vector.Filtering;

public sealed record VectorFilterOr(IReadOnlyList<VectorFilter> Operands) : VectorFilter
{
    public VectorFilterOr(params VectorFilter[] ops) : this((IReadOnlyList<VectorFilter>)ops) { }
}