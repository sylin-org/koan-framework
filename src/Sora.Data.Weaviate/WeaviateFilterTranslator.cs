using Sora.Data.Abstractions;

namespace Sora.Data.Weaviate;

// Internal helper that translates the shared VectorFilter AST into Weaviate GraphQL 'where' syntax
internal static class WeaviateFilterTranslator
{
    public static string TranslateWhereClause(object? filter)
    {
        if (!VectorFilterJson.TryParse(filter, out var ast) || ast is null)
            return string.Empty;

        var gql = Translate(ast);
        return gql ?? string.Empty;
    }

    public static string? Translate(VectorFilter filter)
    {
        switch (filter)
        {
            case VectorFilterAnd and:
                var opsAnd = and.Operands.Select(Translate).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (opsAnd.Length == 0) return null;
                if (opsAnd.Length == 1) return opsAnd[0];
                return $"{{ operator: And, operands: [ {string.Join(",", opsAnd)} ] }}";
            case VectorFilterOr or:
                var opsOr = or.Operands.Select(Translate).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (opsOr.Length == 0) return null;
                if (opsOr.Length == 1) return opsOr[0];
                return $"{{ operator: Or, operands: [ {string.Join(",", opsOr)} ] }}";
            case VectorFilterNot not:
                var inner = Translate(not.Operand);
                if (string.IsNullOrEmpty(inner)) return null;
                return $"{{ operator: Not, operands: [ {inner} ] }}";
            case VectorFilterCompare cmp:
                var path = $"[{string.Join(',', cmp.Path.Select(p => "\"" + EscapeGraphQl(p) + "\""))}]";
                var op = cmp.Operator switch
                {
                    VectorFilterOperator.Eq => "Equal",
                    VectorFilterOperator.Ne => "NotEqual",
                    VectorFilterOperator.Gt => "GreaterThan",
                    VectorFilterOperator.Gte => "GreaterThanEqual",
                    VectorFilterOperator.Lt => "LessThan",
                    VectorFilterOperator.Lte => "LessThanEqual",
                    VectorFilterOperator.Like => "Like",
                    VectorFilterOperator.Contains => "ContainsAny",
                    _ => null
                };
                if (op is null) return null;
                var (field, lit) = ValueFieldAndLiteralFromObject(cmp.Value);
                return $"{{ path: {path}, operator: {op}, {field}: {lit} }}";
            default:
                return null;
        }
    }

    private static (string field, string literal) ValueFieldAndLiteralFromObject(object? value)
    {
        switch (value)
        {
            case null: return ("valueText", "null");
            case string s: return ("valueText", $"\"{EscapeGraphQl(s)}\"");
            case bool b: return ("valueBoolean", b ? "true" : "false");
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return ("valueInt", Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture));
            case float or double or decimal:
                return ("valueNumber", Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture));
            default:
                return ("valueText", $"\"{EscapeGraphQl(value?.ToString())}\"");
        }
    }

    private static string EscapeGraphQl(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
