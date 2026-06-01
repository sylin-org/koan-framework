using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Vector.Filtering;

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Translates Koan's provider-agnostic <see cref="VectorFilter"/> AST into Qdrant's structured
/// filter DSL (must / should / must_not arrays of <c>{ key, match | range }</c> conditions).
///
/// <para>
/// Field paths are dot-flattened. When a path starts with the configured metadata field name,
/// the prefix stays — Qdrant's payload-key resolution natively handles dot-paths into nested
/// objects, so e.g. <c>metadata.category</c> looks up <c>payload.metadata.category</c>.
/// </para>
/// </summary>
internal static class QdrantFilterTranslator
{
    public static JObject? Translate(object? filter, string metadataField)
    {
        if (!VectorFilterJson.TryParse(filter, out var ast) || ast is null)
        {
            return null;
        }

        // Top-level filter object must always have must / should / must_not at the root —
        // even a single comparison becomes `{ "must": [<cond>] }` so the wire shape is
        // predictable.
        return WrapMust(Visit(ast, metadataField));
    }

    private static JObject Visit(VectorFilter filter, string metadataField)
    {
        return filter switch
        {
            VectorFilterAnd and => new JObject
            {
                ["must"] = new JArray(and.Operands.Select(f => (JToken)Visit(f, metadataField)))
            },
            VectorFilterOr or => new JObject
            {
                ["should"] = new JArray(or.Operands.Select(f => (JToken)Visit(f, metadataField)))
            },
            VectorFilterNot not => new JObject
            {
                ["must_not"] = new JArray(Visit(not.Operand, metadataField))
            },
            VectorFilterCompare cmp => TranslateCompare(cmp, metadataField),
            _ => throw new NotSupportedException($"Vector filter '{filter.GetType().Name}' is not supported for Qdrant.")
        };
    }

    private static JObject TranslateCompare(VectorFilterCompare cmp, string metadataField)
    {
        var key = NormalizePath(cmp.Path, metadataField);

        return cmp.Operator switch
        {
            VectorFilterOperator.Eq => new JObject
            {
                ["key"] = key,
                ["match"] = new JObject { ["value"] = ToToken(cmp.Value) }
            },
            VectorFilterOperator.Ne => new JObject
            {
                ["must_not"] = new JArray(new JObject
                {
                    ["key"] = key,
                    ["match"] = new JObject { ["value"] = ToToken(cmp.Value) }
                })
            },
            VectorFilterOperator.Gt => Range(key, "gt", cmp.Value),
            VectorFilterOperator.Gte => Range(key, "gte", cmp.Value),
            VectorFilterOperator.Lt => Range(key, "lt", cmp.Value),
            VectorFilterOperator.Lte => Range(key, "lte", cmp.Value),
            VectorFilterOperator.In => new JObject
            {
                ["key"] = key,
                ["match"] = new JObject { ["any"] = ToArray(cmp.Value) }
            },
            // Qdrant's full-text match needs a text index on the field, which we don't auto-create.
            // For contains/like we fall back to substring match via "text" match — Qdrant treats
            // it as a substring query when no full-text index exists.
            VectorFilterOperator.Contains => new JObject
            {
                ["key"] = key,
                ["match"] = new JObject { ["text"] = ToText(cmp.Value) }
            },
            VectorFilterOperator.Like => new JObject
            {
                ["key"] = key,
                ["match"] = new JObject { ["text"] = ToText(cmp.Value).Replace("%", "") }
            },
            VectorFilterOperator.Between => Between(key, cmp.Value),
            // DATA-0097 F2: fail loud on an operator Qdrant cannot render — never silently treat
            // it as Eq (which would return a wrong/widened result set with no signal).
            _ => throw new NotSupportedException(
                $"Qdrant does not support vector filter operator '{cmp.Operator}' on metadata field '{string.Join(".", cmp.Path)}'.")
        };
    }

    private static JObject Range(string key, string op, object? value)
    {
        return new JObject
        {
            ["key"] = key,
            ["range"] = new JObject { [op] = ToToken(value) }
        };
    }

    private static JObject Between(string key, object? value)
    {
        if (value is IEnumerable<object?> enumerable)
        {
            var list = enumerable.ToList();
            if (list.Count >= 2)
            {
                return new JObject
                {
                    ["key"] = key,
                    ["range"] = new JObject
                    {
                        ["gte"] = ToToken(list[0]),
                        ["lte"] = ToToken(list[1])
                    }
                };
            }
        }
        throw new NotSupportedException("Between filter requires a collection with at least two values.");
    }

    private static JObject WrapMust(JObject inner)
    {
        // A leaf compare returns `{ key, match }` directly; wrap it in a must array. Composite
        // nodes are already shaped as must/should/must_not at the top so we pass them through.
        if (inner.ContainsKey("must") || inner.ContainsKey("should") || inner.ContainsKey("must_not"))
        {
            return inner;
        }
        return new JObject { ["must"] = new JArray(inner) };
    }

    private static string NormalizePath(IReadOnlyList<string> path, string metadataField)
    {
        // Qdrant resolves dot-paths into nested payload structures natively. The kit stores the
        // caller-supplied metadata under `payload.<metadataField>` so e.g. a filter on
        // `metadata.category` works whether the path comes in as ["category"] (treated as
        // payload-root) or ["metadata", "category"] (matches the nested-object layout).
        if (path.Count == 1) return path[0];
        return string.Join('.', path);
    }

    private static JToken ToToken(object? value)
        => value switch
        {
            null => JValue.CreateNull(),
            JToken token => token,
            _ => JToken.FromObject(value)
        };

    private static JArray ToArray(object? value)
    {
        if (value is IEnumerable<object?> enumerable)
        {
            return new JArray(enumerable.Select(ToToken));
        }
        return new JArray(ToToken(value));
    }

    private static string ToText(object? value) => value?.ToString() ?? "";
}
