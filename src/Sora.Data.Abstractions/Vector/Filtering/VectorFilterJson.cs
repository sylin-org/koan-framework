using System.Text.Json;

namespace Sora.Data.Abstractions.Vector.Filtering;

public static class VectorFilterJson
{
    public static bool TryParse(object? input, out VectorFilter? filter)
    {
        filter = null;
        if (input is null) return false;
        if (input is VectorFilter vf) { filter = vf; return true; }
        JsonElement el;
        try { el = JsonSerializer.SerializeToElement(input); }
        catch { return false; }
        filter = Parse(el);
        return filter is not null;
    }

    public static VectorFilter? Parse(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("operator", out var opEl))
            {
                var opStr = opEl.GetString() ?? string.Empty;
                if (string.Equals(opStr, "And", StringComparison.OrdinalIgnoreCase) || string.Equals(opStr, "Or", StringComparison.OrdinalIgnoreCase))
                {
                    if (!el.TryGetProperty("operands", out var opsEl) || opsEl.ValueKind != JsonValueKind.Array) return null;
                    var children = opsEl.EnumerateArray().Select(Parse).Where(c => c is not null)!.Cast<VectorFilter>().ToArray();
                    return string.Equals(opStr, "And", StringComparison.OrdinalIgnoreCase) ? new VectorFilterAnd(children) : new VectorFilterOr(children);
                }
                if (string.Equals(opStr, "Not", StringComparison.OrdinalIgnoreCase))
                {
                    if (!el.TryGetProperty("operands", out var opsEl) || opsEl.ValueKind != JsonValueKind.Array) return null;
                    var first = opsEl.EnumerateArray().Select(Parse).FirstOrDefault();
                    return first is null ? null : new VectorFilterNot(first);
                }
                var path = ReadPath(el);
                if (path is null) return null;
                if (!el.TryGetProperty("value", out var valEl)) return null;
                var (value, _) = ReadValue(valEl);
                var op = NormalizeCompare(opStr);
                return new VectorFilterCompare(path, op, value);
            }
            // equality-map shorthand
            var parts = new List<VectorFilter>();
            foreach (var prop in el.EnumerateObject())
            {
                var (value, ok) = ReadValue(prop.Value);
                if (!ok) continue;
                parts.Add(new VectorFilterCompare(new[] { prop.Name }, VectorFilterOperator.Eq, value));
            }
            if (parts.Count == 0) return null;
            if (parts.Count == 1) return parts[0];
            return new VectorFilterAnd(parts);
        }
        return null;
    }

    public static JsonElement Write(VectorFilter filter)
    {
        using var doc = JsonDocument.Parse(WriteToString(filter));
        return doc.RootElement.Clone();
    }

    public static string WriteToString(VectorFilter filter)
    {
        return filter switch
        {
            VectorFilterAnd and => $"{{ \"operator\": \"And\", \"operands\": [ {string.Join(",", and.Operands.Select(WriteToString))} ] }}",
            VectorFilterOr or => $"{{ \"operator\": \"Or\", \"operands\": [ {string.Join(",", or.Operands.Select(WriteToString))} ] }}",
            VectorFilterNot not => $"{{ \"operator\": \"Not\", \"operands\": [ {WriteToString(not.Operand)} ] }}",
            VectorFilterCompare cmp => WriteLeaf(cmp),
            _ => "{}"
        };
    }

    private static string WriteLeaf(VectorFilterCompare cmp)
    {
        var op = cmp.Operator.ToString();
        var path = "[" + string.Join(',', cmp.Path.Select(p => "\"" + Escape(p) + "\"")) + "]";
        var (field, lit) = ValueFieldAndLiteral(cmp.Value);
        return $"{{ \"path\": {path}, \"operator\": \"{op}\", {field}: {lit} }}";
    }

    private static string Escape(string? s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static (object? value, bool ok) ReadValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return (value.GetString(), true);
            case JsonValueKind.True:
            case JsonValueKind.False: return (value.GetBoolean(), true);
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var i)) return (i, true);
                if (value.TryGetDouble(out var d)) return (d, true);
                return (null, false);
            case JsonValueKind.Null: return (null, true);
            default: return (null, false);
        }
    }

    private static string[]? ReadPath(JsonElement el)
    {
        if (!el.TryGetProperty("path", out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return new[] { p.GetString()! };
        if (p.ValueKind == JsonValueKind.Array) return p.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        return null;
    }

    private static VectorFilterOperator NormalizeCompare(string op)
    {
        return op.ToLowerInvariant() switch
        {
            "eq" or "equal" => VectorFilterOperator.Eq,
            "ne" or "notequal" => VectorFilterOperator.Ne,
            "gt" or "greaterthan" => VectorFilterOperator.Gt,
            "gte" or "ge" or "greaterthanequal" => VectorFilterOperator.Gte,
            "lt" or "lessthan" => VectorFilterOperator.Lt,
            "lte" or "le" or "lessthanequal" => VectorFilterOperator.Lte,
            "like" => VectorFilterOperator.Like,
            "contains" => VectorFilterOperator.Contains,
            _ => Enum.TryParse<VectorFilterOperator>(op, ignoreCase: true, out var e) ? e : VectorFilterOperator.Eq
        };
    }

    private static (string field, string literal) ValueFieldAndLiteral(object? value)
    {
        switch (value)
        {
            case null: return ("\"valueText\"", "null");
            case string s: return ("\"valueText\"", $"\"{Escape(s)}\"");
            case bool b: return ("\"valueBoolean\"", b ? "true" : "false");
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return ("\"valueInt\"", Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture));
            case float or double or decimal:
                return ("\"valueNumber\"", Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture));
            default:
                return ("\"valueText\"", $"\"{Escape(value.ToString())}\"");
        }
    }
}
