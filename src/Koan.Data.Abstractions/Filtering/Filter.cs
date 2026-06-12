using System.Linq.Expressions;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Provider-agnostic, immutable boolean predicate over an entity: the single normalized
/// filter model that both the JSON DSL (<c>JsonFilterParser</c>) and raw LINQ
/// (<c>LinqFilterCompiler</c>) lower into, that every adapter translates through
/// <c>IFilterTranslator</c>, and that <c>InMemoryFilterEvaluator</c> executes as the
/// fallback floor and convergence oracle.
///
/// Closed sealed-record hierarchy: exhaustive matching, structural equality, and no silent
/// operator drift. Promoted and generalized from the former Vector-only filter AST
/// (supersedes DATA-0056). Combinators are deliberately minimal — <c>$nor</c> lowers to
/// <c>Not(AnyOf(...))</c>, <c>$between</c> to <c>AllOf(Gte, Lte)</c>, and wildcard strings to
/// StartsWith/EndsWith/Contains at parse time — so the hierarchy carries no redundant nodes.
/// </summary>
public abstract record Filter
{
    /// <summary>Conjunction builder — every operand must match.</summary>
    public static Filter All(params Filter[] operands) => new AllOf(operands);

    /// <summary>Disjunction builder — at least one operand must match.</summary>
    public static Filter Any(params Filter[] operands) => new AnyOf(operands);

    /// <summary>Negation builder.</summary>
    public static Filter Negate(Filter operand) => new Not(operand);

    /// <summary>Field-predicate builder.</summary>
    public static Filter On(FieldPath field, FilterOperator op, FilterValue value) => new FieldFilter(field, op, value);

    public static Filter Eq(string field, object? value)
        => new FieldFilter(FieldPath.Of(field), FilterOperator.Eq, FilterValue.Of(value));

    public static Filter In(string field, IReadOnlyList<object?> values)
        => new FieldFilter(FieldPath.Of(field), FilterOperator.In, FilterValue.Many(values));

    public static Filter HasAny(string field, IReadOnlyList<object?> values)
        => new FieldFilter(FieldPath.Of(field), FilterOperator.HasAny, FilterValue.Many(values));

    public static Filter HasAll(string field, IReadOnlyList<object?> values)
        => new FieldFilter(FieldPath.Of(field), FilterOperator.HasAll, FilterValue.Many(values));
}

/// <summary>Conjunction — every operand must match.</summary>
public sealed record AllOf(IReadOnlyList<Filter> Operands) : Filter;

/// <summary>Disjunction — at least one operand must match.</summary>
public sealed record AnyOf(IReadOnlyList<Filter> Operands) : Filter;

/// <summary>Negation of a single inner predicate. <c>$nor</c> is modelled as <c>Not(AnyOf(...))</c>.</summary>
public sealed record Not(Filter Operand) : Filter;

/// <summary>
/// A predicate on a single (possibly nested) field of the entity. <paramref name="IgnoreCase"/>
/// requests case-insensitive comparison for string operators (folds in DATA-0031's
/// <c>$options.ignoreCase</c> as a per-node flag rather than a threaded mutable option).
/// </summary>
public sealed record FieldFilter(FieldPath Field, FilterOperator Operator, FilterValue Value, bool IgnoreCase = false) : Filter;

/// <summary>
/// An opaque CLR predicate the front-end could not lower into a translatable node
/// (e.g. an arbitrary lambda body from raw LINQ). It is never pushed down — it is the
/// residual the <c>FilterPushdownCoordinator</c> evaluates in memory via
/// <c>InMemoryFilterEvaluator</c>. Retains the original expression so it stays inspectable.
/// </summary>
public sealed record ClrFilter(LambdaExpression Predicate) : Filter;
