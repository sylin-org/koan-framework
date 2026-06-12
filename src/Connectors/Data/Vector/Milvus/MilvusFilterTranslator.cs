using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Milvus;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a Milvus boolean expression string
/// (AI-0036 §9 / DATA-0097 P1).
/// </summary>
/// <remarks>
/// Milvus has no reliable IS-NULL test for dynamic JSON keys, so it cannot faithfully emit the
/// null-inclusive negations the convergence oracle requires. Per the capability contract, the
/// negation operators (<c>Ne/Nin/HasNone</c>) and the null-sensitive <c>Size/Exists</c> are
/// therefore DECLARED ABSENT — they hard-error at the coordinator rather than silently dropping
/// rows that lack the key. What it does render faithfully: equality/ordering, <c>In</c>, <c>like</c>
/// for StartsWith/EndsWith/Contains, and <c>json_contains*</c> for collection membership. The reader
/// lowers $between/wildcards, so there are no Like/Between arms.
/// </remarks>
internal static class MilvusFilterTranslator
{
    public static readonly FilterSupport Caps = FilterSupport.Uniform(
        nestedPaths: true, ignoreCase: false,
        FilterOperator.Eq,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll);

    public static string? Translate(Filter? filter, string metadataField)
        => filter is null ? null : Visit(filter, metadataField);

    private static string Visit(Filter filter, string metadataField)
    {
        return filter switch
        {
            AllOf and => string.Join(" && ", and.Operands.Select(f => $"({Visit(f, metadataField)})")),
            AnyOf or => string.Join(" || ", or.Operands.Select(f => $"({Visit(f, metadataField)})")),
            Not not => $"!({Visit(not.Operand, metadataField)})",
            FieldFilter cmp => TranslateLeaf(cmp, metadataField),
            _ => throw new NotSupportedException($"Milvus cannot translate filter node '{filter.GetType().Name}'.")
        };
    }

    private static string TranslateLeaf(FieldFilter f, string metadataField)
    {
        var field = NormalizePath(f.Field.Segments, metadataField);
        switch (f.Operator)
        {
            case FilterOperator.Eq: return $"{field} == {Val(Scalar(f))}";
            case FilterOperator.Gt: return $"{field} > {Val(Scalar(f))}";
            case FilterOperator.Gte: return $"{field} >= {Val(Scalar(f))}";
            case FilterOperator.Lt: return $"{field} < {Val(Scalar(f))}";
            case FilterOperator.Lte: return $"{field} <= {Val(Scalar(f))}";
            case FilterOperator.In: return $"{field} in [{string.Join(",", Set(f).Select(Val))}]";
            case FilterOperator.StartsWith: return $"{field} like \"{Like(ScalarStr(f))}%\"";
            case FilterOperator.EndsWith: return $"{field} like \"%{Like(ScalarStr(f))}\"";
            case FilterOperator.Contains: return $"{field} like \"%{Like(ScalarStr(f))}%\"";
            case FilterOperator.Has: return $"json_contains({field}, {Val(Scalar(f))})";
            case FilterOperator.HasAny: return $"json_contains_any({field}, [{string.Join(",", Set(f).Select(Val))}])";
            case FilterOperator.HasAll: return $"json_contains_all({field}, [{string.Join(",", Set(f).Select(Val))}])";
            default:
                throw new NotSupportedException(
                    $"Milvus does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    private static string NormalizePath(IReadOnlyList<string> path, string metadataField)
    {
        // Caller metadata is stored in the JSON column <metadataField>; filter keys index into it as
        // metadata["key"]["k2"] (Milvus JSON access). If the caller already qualified with the metadata
        // field, keep it; otherwise prefix it.
        var segments = path.Count > 0 && path[0] == metadataField ? path.Skip(1).ToList() : path.ToList();
        var sb = new StringBuilder(metadataField);
        foreach (var seg in segments) sb.Append("[\"").Append(seg).Append("\"]");
        return sb.ToString();
    }

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
        _ => Array.Empty<object?>()
    };

    private static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("\"", "\\\"");

    private static string Val(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        bool b => b ? "true" : "false",
        _ when value is Enum => Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable fo => fo.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => $"\"{value.ToString()?.Replace("\"", "\\\"") ?? ""}\""
    };
}
