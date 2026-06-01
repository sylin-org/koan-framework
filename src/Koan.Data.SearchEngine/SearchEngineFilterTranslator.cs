using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.SearchEngine;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a search-engine query DSL <see cref="JObject"/>
/// (AI-0036 §9 / DATA-0097 P1). This is the <b>single source of truth</b> shared by the Elasticsearch
/// and OpenSearch vector connectors — both are built on Apache Lucene and speak the identical query DSL,
/// so the translation was previously duplicated-but-pinned across the two adapters and is now folded
/// here to eliminate drift.
/// </summary>
/// <remarks>
/// Caller metadata is stored nested under the configured metadata field (object, dynamic), so filter
/// fields are prefixed with it; exact-match (term/terms) and wildcard on STRING values target the
/// dynamic <c>.keyword</c> sub-field (Lucene maps strings as analyzed text + a <c>.keyword</c>
/// sub-field, and term/wildcard only match the keyword form). Numeric range and exists target the bare
/// field. Lucene <c>bool/must_not</c> is null-inclusive, so Ne/Nin/HasNone match the convergence oracle.
/// The <c>engine</c> label only personalizes the not-supported exception messages (e.g. "Elasticsearch"
/// vs "OpenSearch") so a failure names the adapter the caller is actually using.
/// </remarks>
public static class SearchEngineFilterTranslator
{
    public static readonly VectorFilterCapabilities Caps = VectorFilterCapabilities.Of(
        nestedPaths: true, ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Exists);

    public static JObject? TranslateWhereClause(Filter? filter, string metadataField, string engine = "Search engine")
        => filter is null ? null : Translate(filter, metadataField, engine);

    public static JObject Translate(Filter filter, string mf, string engine = "Search engine")
    {
        return filter switch
        {
            AllOf and => Bool("must", and.Operands.Select(o => Translate(o, mf, engine))),
            AnyOf or => new JObject { ["bool"] = new JObject { ["should"] = new JArray(or.Operands.Select(o => Translate(o, mf, engine)).Cast<object>().ToArray()), ["minimum_should_match"] = 1 } },
            Not not => new JObject { ["bool"] = new JObject { ["must_not"] = new JArray(Translate(not.Operand, mf, engine)) } },
            FieldFilter cmp => TranslateLeaf(cmp, mf, engine),
            _ => throw new System.NotSupportedException($"{engine} cannot translate filter node '{filter.GetType().Name}'.")
        };
    }

    private static JObject TranslateLeaf(FieldFilter f, string mf, string engine)
    {
        switch (f.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has: // term on an array field matches if it contains the value
                return Term(KeyField(f, mf, Scalar(f)), Scalar(f));
            case FilterOperator.Ne:
                return MustNot(Term(KeyField(f, mf, Scalar(f)), Scalar(f)));
            case FilterOperator.Gt: return Range(Bare(f, mf), "gt", Scalar(f));
            case FilterOperator.Gte: return Range(Bare(f, mf), "gte", Scalar(f));
            case FilterOperator.Lt: return Range(Bare(f, mf), "lt", Scalar(f));
            case FilterOperator.Lte: return Range(Bare(f, mf), "lte", Scalar(f));
            case FilterOperator.In:
            case FilterOperator.HasAny:
                return Terms(KeyField(f, mf, FirstOfSet(f)), Set(f));
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                return MustNot(Terms(KeyField(f, mf, FirstOfSet(f)), Set(f)));
            case FilterOperator.HasAll:
                return Bool("must", Set(f).Select(v => Term(KeyField(f, mf, v), v)));
            case FilterOperator.StartsWith: return Wildcard(Keyword(f, mf), $"{Wild(ScalarStr(f))}*");
            case FilterOperator.EndsWith: return Wildcard(Keyword(f, mf), $"*{Wild(ScalarStr(f))}");
            case FilterOperator.Contains: return Wildcard(Keyword(f, mf), $"*{Wild(ScalarStr(f))}*");
            case FilterOperator.Exists:
                var present = Scalar(f) is not bool b || b;
                var exists = new JObject { ["exists"] = new JObject { ["field"] = Bare(f, mf) } };
                return present ? exists : MustNot(exists);
            default:
                throw new System.NotSupportedException(
                    $"{engine} does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    // metadata.<path> (bare) — for range/exists.
    private static string Bare(FieldFilter f, string mf) => mf + "." + string.Join('.', f.Field.Segments);
    // metadata.<path>.keyword (always) — for wildcard.
    private static string Keyword(FieldFilter f, string mf) => Bare(f, mf) + ".keyword";
    // metadata.<path>[.keyword] — keyword only when matching a string value (numbers/bools use the bare field).
    private static string KeyField(FieldFilter f, string mf, object? sample) => sample is string ? Keyword(f, mf) : Bare(f, mf);

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
    private static object? FirstOfSet(FieldFilter f) => Set(f).Count > 0 ? Set(f)[0] : null;

    private static string Wild(string s) => s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?");

    private static JToken ToToken(object? value) => value switch
    {
        null => JValue.CreateNull(),
        JToken token => token,
        _ => JToken.FromObject(value)
    };
}
