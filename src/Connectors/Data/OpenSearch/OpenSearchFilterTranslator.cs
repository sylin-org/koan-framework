using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.OpenSearch;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into an OpenSearch query DSL JObject
/// (AI-0036 §10 / DATA-0097 P1). Identical to the Elasticsearch translator (same Lucene query DSL);
/// kept duplicated-but-capability-pinned rather than coupling the two connector packages. Lucene
/// <c>bool/must_not</c> is null-inclusive, so <c>Ne/Nin/HasNone</c> match the convergence oracle.
/// </summary>
internal static class OpenSearchFilterTranslator
{
    public static readonly VectorFilterCapabilities Caps = VectorFilterCapabilities.Of(
        nestedPaths: true, ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Exists);

    public static JObject? TranslateWhereClause(Filter? filter)
        => filter is null ? null : Translate(filter);

    public static JObject Translate(Filter filter)
    {
        return filter switch
        {
            AllOf and => Bool("must", and.Operands.Select(Translate)),
            AnyOf or => new JObject { ["bool"] = new JObject { ["should"] = new JArray(or.Operands.Select(Translate).Cast<object>().ToArray()), ["minimum_should_match"] = 1 } },
            Not not => new JObject { ["bool"] = new JObject { ["must_not"] = new JArray(Translate(not.Operand)) } },
            FieldFilter cmp => TranslateLeaf(cmp),
            _ => throw new System.NotSupportedException($"OpenSearch cannot translate filter node '{filter.GetType().Name}'.")
        };
    }

    private static JObject TranslateLeaf(FieldFilter f)
    {
        var field = string.Join('.', f.Field.Segments);
        switch (f.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has:
                return Term(field, Scalar(f));
            case FilterOperator.Ne:
                return MustNot(Term(field, Scalar(f)));
            case FilterOperator.Gt: return Range(field, "gt", Scalar(f));
            case FilterOperator.Gte: return Range(field, "gte", Scalar(f));
            case FilterOperator.Lt: return Range(field, "lt", Scalar(f));
            case FilterOperator.Lte: return Range(field, "lte", Scalar(f));
            case FilterOperator.In:
            case FilterOperator.HasAny:
                return Terms(field, Set(f));
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                return MustNot(Terms(field, Set(f)));
            case FilterOperator.HasAll:
                return Bool("must", Set(f).Select(v => Term(field, v)));
            case FilterOperator.StartsWith: return Wildcard(field, $"{Wild(ScalarStr(f))}*");
            case FilterOperator.EndsWith: return Wildcard(field, $"*{Wild(ScalarStr(f))}");
            case FilterOperator.Contains: return Wildcard(field, $"*{Wild(ScalarStr(f))}*");
            case FilterOperator.Exists:
                var present = Scalar(f) is not bool b || b;
                var exists = new JObject { ["exists"] = new JObject { ["field"] = field } };
                return present ? exists : MustNot(exists);
            default:
                throw new System.NotSupportedException(
                    $"OpenSearch does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    private static JObject Bool(string clause, IEnumerable<JObject> parts)
        => new() { ["bool"] = new JObject { [clause] = new JArray(parts.Cast<object>().ToArray()) } };

    private static JObject MustNot(JObject inner)
        => new() { ["bool"] = new JObject { ["must_not"] = new JArray(inner) } };

    private static JObject Term(string field, object? value)
        => new() { ["term"] = new JObject { [field] = ToToken(value) } };

    private static JObject Terms(string field, IReadOnlyList<object?> values)
        => new() { ["terms"] = new JObject { [field] = new JArray(values.Select(ToToken).Cast<object>().ToArray()) } };

    private static JObject Range(string field, string op, object? value)
        => new() { ["range"] = new JObject { [field] = new JObject { [op] = ToToken(value) } } };

    private static JObject Wildcard(string field, string pattern)
        => new() { ["wildcard"] = new JObject { [field] = pattern } };

    private static object? Scalar(FieldFilter f) => f.Value switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static string ScalarStr(FieldFilter f) => Scalar(f)?.ToString() ?? "";

    private static IReadOnlyList<object?> Set(FieldFilter f) => f.Value switch
    {
        FilterValue.Set st => st.Values,
        FilterValue.Scalar s => new[] { s.Value },
        _ => System.Array.Empty<object?>()
    };

    private static string Wild(string s) => s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?");

    private static JToken ToToken(object? value) => value switch
    {
        null => JValue.CreateNull(),
        JToken token => token,
        _ => JToken.FromObject(value)
    };
}
