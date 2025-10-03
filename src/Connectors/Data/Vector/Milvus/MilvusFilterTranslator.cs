using System;
using System.Collections.Generic;
using System.Linq;

using System.Text;
using Koan.Data.Abstractions.Vector.Filtering;

namespace Koan.Data.Vector.Connector.Milvus;

internal static class MilvusFilterTranslator
{
    public static string? Translate(object? filter, string metadataField)
    {
        if (!VectorFilterJson.TryParse(filter, out var ast) || ast is null)
        {
            return null;
        }

        return Visit(ast, metadataField);
    }

    private static string Visit(VectorFilter filter, string metadataField)
    {
        return filter switch
        {
            VectorFilterAnd and => string.Join(" && ", and.Operands.Select(f => $"({Visit(f, metadataField)})")),
            VectorFilterOr or => string.Join(" || ", or.Operands.Select(f => $"({Visit(f, metadataField)})")),
            VectorFilterNot not => $"!({Visit(not.Operand, metadataField)})",
            VectorFilterCompare cmp => TranslateCompare(cmp, metadataField),
            _ => throw new NotSupportedException($"Filter '{filter.GetType().Name}' is not supported for Milvus.")
        };
    }

    private static string TranslateCompare(VectorFilterCompare cmp, string metadataField)
    {
        var field = NormalizePath(cmp.Path, metadataField);
        var value = FormatValue(cmp.Value);

        return cmp.Operator switch
        {
            VectorFilterOperator.Eq => $"{field} == {value}",
            VectorFilterOperator.Ne => $"{field} != {value}",
            VectorFilterOperator.Gt => $"{field} > {value}",
            VectorFilterOperator.Gte => $"{field} >= {value}",
            VectorFilterOperator.Lt => $"{field} < {value}",
            VectorFilterOperator.Lte => $"{field} <= {value}",
            VectorFilterOperator.Contains => $"contains({field}, {value})",
            VectorFilterOperator.In => TranslateIn(field, cmp.Value),
            VectorFilterOperator.Like => $"like({field}, {value})",
            VectorFilterOperator.Between => TranslateBetween(field, cmp.Value),
            _ => $"{field} == {value}"
        };
    }

    private static string TranslateIn(string field, object? value)
    {
        if (value is IEnumerable<object?> enumerable)
        {
            var items = enumerable.Select(FormatValue).ToArray();
            return $"{field} in [{string.Join(",", items)}]";
        }

        return $"{field} == {FormatValue(value)}";
    }

    private static string TranslateBetween(string field, object? value)
    {
        if (value is IEnumerable<object?> enumerable)
        {
            var vals = enumerable.Take(2).Select(FormatValue).ToArray();
            if (vals.Length == 2)
            {
                return $"{field} >= {vals[0]} && {field} <= {vals[1]}";
            }
        }

        throw new NotSupportedException("Between filter requires two values for Milvus expressions.");
    }

    private static string NormalizePath(IReadOnlyList<string> path, string metadataField)
    {
        if (path.Count == 1)
        {
            return path[0];
        }

        if (path[0] == metadataField)
        {
            var builder = new StringBuilder(metadataField);
            for (var i = 1; i < path.Count; i++)
            {
                builder.Append("['").Append(path[i]).Append("']");
            }
            return builder.ToString();
        }

        return string.Join('.', path);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s.Replace("\"", "\\\"")}\"",
            bool b => b ? "true" : "false",
            _ when value is Enum => Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => $"\"{value?.ToString()?.Replace("\"", "\\\"") ?? string.Empty}\""
        };
    }
}

