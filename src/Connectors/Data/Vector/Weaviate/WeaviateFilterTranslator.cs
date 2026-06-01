using System.Globalization;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Weaviate;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into Weaviate GraphQL <c>where</c> syntax
/// (AI-0036 §10 / DATA-0097 P1). Weaviate is the intentionally REDUCED reference: it declares fewer
/// operators than PGVector, so the coordinator hard-errors on the rest — exercising the
/// residual-is-error path. <c>Ne</c> is rendered null-inclusively as <c>Not(Equal)</c> (Weaviate Not
/// includes rows lacking the property); <c>Exists</c> maps to <c>IsNull</c>; wildcards map to
/// <c>Like</c>. <c>In/Nin/HasNone/Size</c> are deliberately absent. The reader lowers $between/
/// wildcards, so there are no Like/Between operator arms.
/// </summary>
internal static class WeaviateFilterTranslator
{
    public static readonly VectorFilterCapabilities Caps = VectorFilterCapabilities.Of(
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
                return $"{{ operator: Not, operands: [ {Translate(not.Operand)} ] }}";
            case FieldFilter cmp:
                return TranslateLeaf(cmp);
            default:
                throw new System.NotSupportedException($"Weaviate cannot translate filter node '{filter.GetType().Name}'.");
        }
    }

    private static string TranslateLeaf(FieldFilter f)
    {
        var path = $"[{string.Join(',', f.Field.Segments.Select(p => "\"" + Esc(p) + "\""))}]";
        switch (f.Operator)
        {
            case FilterOperator.Eq: return Leaf(path, "Equal", Scalar(f));
            case FilterOperator.Ne: // null-inclusive: Weaviate Not includes rows lacking the value
                return $"{{ operator: Not, operands: [ {Leaf(path, "Equal", Scalar(f))} ] }}";
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
}
