using System.Text;
using Dapper;
using Koan.Data.Abstractions.Vector.Filtering;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Translates the provider-agnostic <see cref="VectorFilter"/> AST into a parameterized SQL
/// predicate over the JSONB <c>metadata</c> column (DATA-0097 F4 fix). This replaces the legacy
/// path that JSON-serialized the raw filter object and ran <c>metadata @&gt; @filter::jsonb</c> —
/// which silently matched nothing when handed a typed AST and could express only equality.
///
/// PGVector has the richest filter substrate of any vector adapter (full SQL over JSONB), so it
/// supports the complete scalar operator set rather than the fewest. It is <b>fail-loud</b>: an
/// operator/shape it cannot render throws <see cref="NotSupportedException"/> — never a silent
/// match-all (the bug class DATA-0097 eliminates). A null filter yields no predicate (full kNN).
/// </summary>
internal static class PGVectorFilterTranslator
{
    /// <summary>
    /// Build a WHERE-clause fragment (without the "WHERE" keyword) from the AST, binding values
    /// into <paramref name="parameters"/>. Returns null when there is no filter to apply.
    /// </summary>
    public static string? Translate(VectorFilter? filter, DynamicParameters parameters)
    {
        if (filter is null) return null;
        var sb = new StringBuilder();
        var counter = 0;
        Visit(filter, sb, parameters, ref counter);
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void Visit(VectorFilter filter, StringBuilder sb, DynamicParameters parameters, ref int counter)
    {
        switch (filter)
        {
            case VectorFilterAnd and:
                Compose(and.Operands, "AND", sb, parameters, ref counter);
                break;
            case VectorFilterOr or:
                Compose(or.Operands, "OR", sb, parameters, ref counter);
                break;
            case VectorFilterNot not:
                sb.Append("NOT (");
                Visit(not.Operand, sb, parameters, ref counter);
                sb.Append(')');
                break;
            case VectorFilterCompare cmp:
                sb.Append(TranslateCompare(cmp, parameters, ref counter));
                break;
            default:
                throw new NotSupportedException(
                    $"PGVector does not support vector filter node '{filter.GetType().Name}'.");
        }
    }

    private static void Compose(IReadOnlyList<VectorFilter> operands, string op, StringBuilder sb, DynamicParameters parameters, ref int counter)
    {
        if (operands.Count == 0) { sb.Append(op == "AND" ? "TRUE" : "FALSE"); return; }
        sb.Append('(');
        for (var i = 0; i < operands.Count; i++)
        {
            if (i > 0) sb.Append(' ').Append(op).Append(' ');
            Visit(operands[i], sb, parameters, ref counter);
        }
        sb.Append(')');
    }

    private static string TranslateCompare(VectorFilterCompare cmp, DynamicParameters parameters, ref int counter)
    {
        // metadata path -> JSONB text accessor: metadata->>'field' (nested: #>>'{a,b}').
        var path = cmp.Path;
        var accessor = path.Count == 1
            ? $"metadata->>'{Sanitize(path[0])}'"
            : $"metadata#>>'{{{string.Join(",", path.Select(Sanitize))}}}'";

        var p = $"f{counter++}";
        switch (cmp.Operator)
        {
            case VectorFilterOperator.Eq:
                parameters.Add(p, ToParam(cmp.Value));
                return $"{accessor} = @{p}";
            case VectorFilterOperator.Ne:
                parameters.Add(p, ToParam(cmp.Value));
                return $"({accessor} IS NULL OR {accessor} <> @{p})";
            case VectorFilterOperator.Gt:
                parameters.Add(p, ToParam(cmp.Value));
                return $"({accessor})::numeric > @{p}";
            case VectorFilterOperator.Gte:
                parameters.Add(p, ToParam(cmp.Value));
                return $"({accessor})::numeric >= @{p}";
            case VectorFilterOperator.Lt:
                parameters.Add(p, ToParam(cmp.Value));
                return $"({accessor})::numeric < @{p}";
            case VectorFilterOperator.Lte:
                parameters.Add(p, ToParam(cmp.Value));
                return $"({accessor})::numeric <= @{p}";
            case VectorFilterOperator.Like:
                parameters.Add(p, ToParam(cmp.Value));
                return $"{accessor} LIKE @{p}";
            case VectorFilterOperator.Contains:
                parameters.Add(p, $"%{cmp.Value}%");
                return $"{accessor} LIKE @{p}";
            case VectorFilterOperator.In:
                parameters.Add(p, ToArray(cmp.Value));
                return $"{accessor} = ANY(@{p})";
            case VectorFilterOperator.Between:
                var arr = ToArray(cmp.Value);
                if (arr.Length != 2)
                    throw new NotSupportedException("PGVector 'Between' requires exactly two bounds.");
                var lo = $"f{counter++}"; var hi = $"f{counter++}";
                parameters.Add(lo, arr[0]); parameters.Add(hi, arr[1]);
                return $"({accessor})::numeric BETWEEN @{lo} AND @{hi}";
            default:
                // Fail loud — no silent Eq fallthrough (DATA-0097 F2).
                throw new NotSupportedException(
                    $"PGVector does not support vector filter operator '{cmp.Operator}' on metadata field '{string.Join(".", path)}'.");
        }
    }

    private static string ToParam(object? value) => value?.ToString() ?? "";

    private static string[] ToArray(object? value)
    {
        if (value is System.Collections.IEnumerable en and not string)
            return en.Cast<object?>().Select(v => v?.ToString() ?? "").ToArray();
        return value is null ? Array.Empty<string>() : new[] { value.ToString() ?? "" };
    }

    // metadata keys are developer-controlled identifiers; reject quotes/backslashes defensively
    // since they are interpolated into the JSONB path literal (values are always parameterized).
    private static string Sanitize(string segment)
    {
        if (segment.IndexOfAny(new[] { '\'', '"', '\\', '{', '}' }) >= 0)
            throw new NotSupportedException($"Invalid metadata field name '{segment}'.");
        return segment;
    }
}
