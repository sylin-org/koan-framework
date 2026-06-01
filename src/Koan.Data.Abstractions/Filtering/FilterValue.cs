namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// The right-hand side of a <see cref="FieldFilter"/>: a single <see cref="Scalar"/>, an
/// ordered <see cref="Set"/> (for In/Nin/HasAny/HasAll/HasNone), or <see cref="None"/>
/// (for <see cref="FilterOperator.Exists"/>). A closed hierarchy so translators and the
/// evaluator match exhaustively rather than inspecting loosely-typed objects.
/// </summary>
public abstract record FilterValue
{
    public static FilterValue Of(object? value) => new Scalar(value);
    public static FilterValue Many(IReadOnlyList<object?> values) => new Set(values);
    public static FilterValue Absent { get; } = new None();

    /// <summary>A single comparison value.</summary>
    public sealed record Scalar(object? Value) : FilterValue;

    /// <summary>An ordered set of candidate values.</summary>
    public sealed record Set(IReadOnlyList<object?> Values) : FilterValue;

    /// <summary>No value (used by <see cref="FilterOperator.Exists"/>).</summary>
    public sealed record None : FilterValue;
}
