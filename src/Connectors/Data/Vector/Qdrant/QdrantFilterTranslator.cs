using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into Qdrant's structured filter DSL
/// (must / should / must_not arrays of <c>{ key, match | range | values_count | is_empty }</c>),
/// per AI-0036 §10 / DATA-0097 P1.
/// </summary>
/// <remarks>
/// Qdrant's <c>must_not</c> is naturally null-inclusive (a point lacking the key satisfies a negated
/// match), so <c>Ne/Nin/HasNone</c> match the convergence oracle's locked null semantics. A
/// <c>match</c> on an array-valued key means "contains", so <c>Has</c>/<c>HasAny</c> reuse the
/// Eq/In shapes. It declares only operators it can render faithfully; StartsWith/EndsWith/Contains
/// are deliberately ABSENT (Qdrant text matching needs a full-text payload index and is neither
/// anchored nor a raw substring match — declaring them would silently mis-match), so they hard-error
/// at the coordinator. The reader lowers $between/wildcards, so there are no Like/Between arms.
/// </remarks>
internal static class QdrantFilterTranslator
{
    public static readonly VectorFilterCapabilities Caps = VectorFilterCapabilities.Of(
        nestedPaths: true, ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size, FilterOperator.Exists);

    public static JObject? Translate(Filter? filter, string metadataField)
    {
        if (filter is null) return null;
        return WrapMust(Visit(filter, metadataField));
    }

    private static JObject Visit(Filter filter, string metadataField)
    {
        return filter switch
        {
            AllOf and => new JObject { ["must"] = new JArray(and.Operands.Select(f => (JToken)Visit(f, metadataField))) },
            AnyOf or => new JObject { ["should"] = new JArray(or.Operands.Select(f => (JToken)Visit(f, metadataField))) },
            Not not => new JObject { ["must_not"] = new JArray(Visit(not.Operand, metadataField)) },
            FieldFilter cmp => TranslateLeaf(cmp, metadataField),
            _ => throw new NotSupportedException($"Qdrant cannot translate filter node '{filter.GetType().Name}'.")
        };
    }

    private static JObject TranslateLeaf(FieldFilter f, string metadataField)
    {
        var key = NormalizePath(f.Field.Segments, metadataField);
        switch (f.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has: // match on an array key == contains
                return Match(key, Scalar(f));
            case FilterOperator.Ne:
                return new JObject { ["must_not"] = new JArray(Match(key, Scalar(f))) };
            case FilterOperator.Gt: return Range(key, "gt", Scalar(f));
            case FilterOperator.Gte: return Range(key, "gte", Scalar(f));
            case FilterOperator.Lt: return Range(key, "lt", Scalar(f));
            case FilterOperator.Lte: return Range(key, "lte", Scalar(f));
            case FilterOperator.In:
            case FilterOperator.HasAny:
                return new JObject { ["key"] = key, ["match"] = new JObject { ["any"] = Arr(f) } };
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                return new JObject { ["must_not"] = new JArray(new JObject { ["key"] = key, ["match"] = new JObject { ["any"] = Arr(f) } }) };
            case FilterOperator.HasAll:
                // composed as conjunction of contains-matches (Qdrant has no native "contains all")
                return new JObject { ["must"] = new JArray(Set(f).Select(v => (JToken)Match(key, v))) };
            case FilterOperator.Size:
                var n = ToToken(Scalar(f));
                return new JObject { ["key"] = key, ["values_count"] = new JObject { ["gte"] = n, ["lte"] = n } };
            case FilterOperator.Exists:
                var present = Scalar(f) is not bool b || b;
                var isEmpty = new JObject { ["is_empty"] = new JObject { ["key"] = key } };
                return present ? new JObject { ["must_not"] = new JArray(isEmpty) } : isEmpty;
            default:
                throw new NotSupportedException(
                    $"Qdrant does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    private static JObject Match(string key, object? value)
        => new() { ["key"] = key, ["match"] = new JObject { ["value"] = ToToken(value) } };

    private static JObject Range(string key, string op, object? value)
        => new() { ["key"] = key, ["range"] = new JObject { [op] = ToToken(value) } };

    private static JObject WrapMust(JObject inner)
        => inner.ContainsKey("must") || inner.ContainsKey("should") || inner.ContainsKey("must_not")
            ? inner
            : new JObject { ["must"] = new JArray(inner) };

    private static string NormalizePath(IReadOnlyList<string> path, string metadataField)
    {
        // Caller metadata is stored nested under payload.<metadataField> (BuildPoint), so a filter
        // key must be prefixed with it — Qdrant navigates nested payload via dot-paths. If the caller
        // already qualified the path with the metadata field, leave it as-is.
        if (path.Count > 0 && path[0] == metadataField) return string.Join('.', path);
        return metadataField + "." + string.Join('.', path);
    }

    private static object? Scalar(FieldFilter f) => f.Value switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter f) => f.Value switch
    {
        FilterValue.Set st => st.Values,
        FilterValue.Scalar s => new[] { s.Value },
        _ => Array.Empty<object?>()
    };

    private static JArray Arr(FieldFilter f) => new(Set(f).Select(ToToken));

    private static JToken ToToken(object? value) => value switch
    {
        null => JValue.CreateNull(),
        JToken token => token,
        _ => JToken.FromObject(value)
    };
}
