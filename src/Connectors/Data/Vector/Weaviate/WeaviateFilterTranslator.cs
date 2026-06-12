using System.Globalization;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Weaviate;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into Weaviate GraphQL <c>where</c> syntax
/// (AI-0036 §9 / DATA-0097 P1). Weaviate is the intentionally REDUCED reference: it declares fewer
/// operators than PGVector, so the coordinator hard-errors on the rest — exercising the
/// residual-is-error path. <c>Ne</c> is rendered null-inclusively as <c>Not(Equal)</c> (Weaviate Not
/// includes rows lacking the property); <c>Exists</c> maps to <c>IsNull</c>; wildcards map to
/// <c>Like</c>. <c>In/Nin/HasNone/Size</c> are deliberately absent. The reader lowers $between/
/// wildcards, so there are no Like/Between operator arms.
/// </summary>
internal static class WeaviateFilterTranslator
{
    public static readonly FilterSupport Caps = FilterSupport.Uniform(
        nestedPaths: true, ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll,
        FilterOperator.Exists);

    public static string TranslateWhereClause(Filter? filter)
        => filter is null ? "" : Translate(filter);

    public static string Translate(Filter filter)
    {
        switch (filter)
        {
            case AllOf and:
                // No silent drop of children: with a coordinator-validated tree every child renders.
                return $"{{ operator: And, operands: [ {string.Join(",", and.Operands.Select(Translate))} ] }}";
            case AnyOf or:
                return $"{{ operator: Or, operands: [ {string.Join(",", or.Operands.Select(Translate))} ] }}";
            case Not not:
                // Weaviate has no generic Not operator — eliminate it via De Morgan down to leaf negations.
                return Negate(not.Operand);
            case FieldFilter cmp:
                return TranslateLeaf(cmp);
            default:
                throw new System.NotSupportedException($"Weaviate cannot translate filter node '{filter.GetType().Name}'.");
        }
    }

    // Weaviate normalizes property names to camelCase (first letter lowercased); match the stored form.
    private static string Path(FieldFilter f)
        => $"[{string.Join(',', f.Field.Segments.Select(p => "\"" + Esc(LowerFirst(p)) + "\""))}]";

    private static string TranslateLeaf(FieldFilter f)
    {
        var path = Path(f);
        switch (f.Operator)
        {
            case FilterOperator.Eq: return Leaf(path, "Equal", Scalar(f));
            case FilterOperator.Ne: // null-inclusive: NotEqual OR IsNull (Weaviate NotEqual excludes missing)
                return OrNull(Leaf(path, "NotEqual", Scalar(f)), path);
            case FilterOperator.Gt: return Leaf(path, "GreaterThan", Scalar(f));
            case FilterOperator.Gte: return Leaf(path, "GreaterThanEqual", Scalar(f));
            case FilterOperator.Lt: return Leaf(path, "LessThan", Scalar(f));
            case FilterOperator.Lte: return Leaf(path, "LessThanEqual", Scalar(f));
            case FilterOperator.StartsWith: return Leaf(path, "Like", $"{ScalarStr(f)}*");
            case FilterOperator.EndsWith: return Leaf(path, "Like", $"*{ScalarStr(f)}");
            case FilterOperator.Contains: return Leaf(path, "Like", $"*{ScalarStr(f)}*");
            case FilterOperator.Has: return $"{{ path: {path}, operator: ContainsAny, {TextArray(new object?[] { Scalar(f) })} }}";
            case FilterOperator.HasAny: return $"{{ path: {path}, operator: ContainsAny, {TextArray(Set(f))} }}";
            case FilterOperator.HasAll: return $"{{ path: {path}, operator: ContainsAll, {TextArray(Set(f))} }}";
            case FilterOperator.Exists:
                var present = Scalar(f) is not bool b || b;
                return $"{{ path: {path}, operator: IsNull, valueBoolean: {(present ? "false" : "true")} }}";
            default:
                throw new System.NotSupportedException(
                    $"Weaviate does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    // De Morgan negation (Weaviate lacks a generic Not). Leaf negations are null-inclusive (Or IsNull)
    // to match the locked oracle semantics (e.g. Not(Eq) matches rows lacking the property).
    private static string Negate(Filter f) => f switch
    {
        AllOf and => $"{{ operator: Or, operands: [ {string.Join(",", and.Operands.Select(Negate))} ] }}",
        AnyOf or => $"{{ operator: And, operands: [ {string.Join(",", or.Operands.Select(Negate))} ] }}",
        Not n => Translate(n.Operand),
        FieldFilter leaf => NegateLeaf(leaf),
        _ => throw new System.NotSupportedException($"Weaviate cannot negate filter node '{f.GetType().Name}'.")
    };

    private static string NegateLeaf(FieldFilter f)
    {
        var path = Path(f);
        switch (f.Operator)
        {
            case FilterOperator.Eq: return OrNull(Leaf(path, "NotEqual", Scalar(f)), path);
            case FilterOperator.Ne: return Leaf(path, "Equal", Scalar(f));
            case FilterOperator.Gt: return OrNull(Leaf(path, "LessThanEqual", Scalar(f)), path);
            case FilterOperator.Gte: return OrNull(Leaf(path, "LessThan", Scalar(f)), path);
            case FilterOperator.Lt: return OrNull(Leaf(path, "GreaterThanEqual", Scalar(f)), path);
            case FilterOperator.Lte: return OrNull(Leaf(path, "GreaterThan", Scalar(f)), path);
            case FilterOperator.Exists:
                var present = Scalar(f) is not bool b || b;
                return $"{{ path: {path}, operator: IsNull, valueBoolean: {(present ? "true" : "false")} }}";
            default:
                throw new System.NotSupportedException(
                    $"Weaviate cannot negate vector filter operator '{f.Operator}' on '{f.Field}'.");
        }
    }

    // <clause> OR the property is missing — null-inclusive negation (matches the oracle's locked semantics).
    private static string OrNull(string clause, string path)
        => $"{{ operator: Or, operands: [ {clause}, {{ path: {path}, operator: IsNull, valueBoolean: true }} ] }}";

    private static string Leaf(string path, string op, object? value)
    {
        var (field, lit) = Literal(value);
        return $"{{ path: {path}, operator: {op}, {field}: {lit} }}";
    }

    private static (string field, string literal) Literal(object? value) => value switch
    {
        null => ("valueText", "null"),
        string s => ("valueText", $"\"{Esc(s)}\""),
        bool b => ("valueBoolean", b ? "true" : "false"),
        sbyte or byte or short or ushort or int or uint or long or ulong
            => ("valueInt", System.Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
        float or double or decimal
            => ("valueNumber", System.Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
        _ => ("valueText", $"\"{Esc(value.ToString())}\"")
    };

    private static string TextArray(IReadOnlyList<object?> values)
        => $"valueText: [ {string.Join(",", values.Select(v => "\"" + Esc(v?.ToString()) + "\""))} ]";

    private static object? Scalar(FieldFilter f) => f.Value switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static string ScalarStr(FieldFilter f) => Esc(Scalar(f)?.ToString());

    private static IReadOnlyList<object?> Set(FieldFilter f) => f.Value switch
    {
        FilterValue.Set st => st.Values,
        FilterValue.Scalar s => new[] { s.Value },
        _ => System.Array.Empty<object?>()
    };

    private static string Esc(string? value)
        => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string LowerFirst(string name)
        => name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
