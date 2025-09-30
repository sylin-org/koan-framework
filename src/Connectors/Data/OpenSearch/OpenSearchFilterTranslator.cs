using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Vector.Filtering;

namespace Koan.Data.Connector.OpenSearch;

internal static class OpenSearchFilterTranslator
{
    public static JObject? TranslateWhereClause(object? filter)
    {
        if (!VectorFilterJson.TryParse(filter, out var ast) || ast is null)
        {
            return null;
        }

        return Translate(ast);
    }

    private static JObject Translate(VectorFilter filter)
    {
        return filter switch
        {
            VectorFilterAnd and => new JObject
            {
                ["bool"] = new JObject
                {
                    ["must"] = new JArray(and.Operands.Select(Translate))
                }
            },
            VectorFilterOr or => new JObject
            {
                ["bool"] = new JObject
                {
                    ["should"] = new JArray(or.Operands.Select(Translate)),
                    ["minimum_should_match"] = 1
                }
            },
            VectorFilterNot not => new JObject
            {
                ["bool"] = new JObject
                {
                    ["must_not"] = new JArray(Translate(not.Operand))
                }
            },
            VectorFilterCompare cmp => TranslateCompare(cmp),
            _ => throw new NotSupportedException($"Vector filter '{filter.GetType().Name}' is not supported for OpenSearch.")
        };
    }

    private static JObject TranslateCompare(VectorFilterCompare cmp)
    {
        var field = string.Join('.', cmp.Path);
        return cmp.Operator switch
        {
            VectorFilterOperator.Eq => new JObject
            {
                ["term"] = new JObject { [field] = ToToken(cmp.Value) }
            },
            VectorFilterOperator.Ne => new JObject
            {
                ["bool"] = new JObject
                {
                    ["must_not"] = new JArray(new JObject
                    {
                        ["term"] = new JObject { [field] = ToToken(cmp.Value) }
                    })
                }
            },
            VectorFilterOperator.Gt => Range(field, "gt", cmp.Value),
            VectorFilterOperator.Gte => Range(field, "gte", cmp.Value),
            VectorFilterOperator.Lt => Range(field, "lt", cmp.Value),
            VectorFilterOperator.Lte => Range(field, "lte", cmp.Value),
            VectorFilterOperator.Like => new JObject
            {
                ["wildcard"] = new JObject { [field] = Pattern(cmp.Value) }
            },
            VectorFilterOperator.Contains => new JObject
            {
                ["match_phrase"] = new JObject { [field] = ToToken(cmp.Value) }
            },
            VectorFilterOperator.In => new JObject
            {
                ["terms"] = new JObject { [field] = ToArray(cmp.Value) }
            },
            VectorFilterOperator.Between => Between(field, cmp.Value),
            _ => new JObject
            {
                ["term"] = new JObject { [field] = ToToken(cmp.Value) }
            }
        };
    }

    private static JObject Range(string field, string op, object? value)
    {
        return new JObject
        {
            ["range"] = new JObject
            {
                [field] = new JObject
                {
                    [op] = ToToken(value)
                }
            }
        };
    }

    private static JObject Between(string field, object? value)
    {
        if (value is IEnumerable<object?> enumerable)
        {
            var list = enumerable.ToList();
            if (list.Count >= 2)
            {
                return new JObject
                {
                    ["range"] = new JObject
                    {
                        [field] = new JObject
                        {
                            ["gte"] = ToToken(list[0]),
                            ["lte"] = ToToken(list[1])
                        }
                    }
                };
            }
        }

        throw new NotSupportedException("Between filter requires a collection with at least two values.");
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

    private static string Pattern(object? value)
    {
        var raw = value?.ToString() ?? string.Empty;
        return raw.Replace('%', '*');
    }
}

