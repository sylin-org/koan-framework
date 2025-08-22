namespace Sora.Data.Abstractions;

// Typed, provider-agnostic vector filter AST root with terse builders.
public abstract record VectorFilter
{
    public static VectorFilter And(params VectorFilter[] operands) => new VectorFilterAnd(operands);
    public static VectorFilter Or(params VectorFilter[] operands) => new VectorFilterOr(operands);
    public static VectorFilter Not(VectorFilter operand) => new VectorFilterNot(operand);

    public static VectorFilter Eq(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Eq, value);
    public static VectorFilter Eq(string[] path, object? value) => new VectorFilterCompare(path, VectorFilterOperator.Eq, value);
    public static VectorFilter Ne(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Ne, value);
    public static VectorFilter Gt(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Gt, value);
    public static VectorFilter Gte(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Gte, value);
    public static VectorFilter Lt(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Lt, value);
    public static VectorFilter Lte(string path, object? value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Lte, value);
    public static VectorFilter Like(string path, string pattern) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Like, pattern);
    public static VectorFilter Contains(string path, string value) => new VectorFilterCompare(new[] { path }, VectorFilterOperator.Contains, value);
}
