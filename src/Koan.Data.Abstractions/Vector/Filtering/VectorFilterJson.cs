using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Abstractions.Vector.Filtering;

public static class VectorFilterJson
{
    public static bool TryParse(object? input, out VectorFilter? filter)
    {
        filter = null;
        if (input is null) return false;
        if (input is VectorFilter vf) { filter = vf; return true; }
        try
        {
            JToken token = input switch
            {
                JToken jt => jt,
                string s => JToken.Parse(s),
                _ => JToken.FromObject(input)
            };
            filter = Parse(token);
            return filter is not null;
        }
        catch { return false; }
    }

    public static VectorFilter? Parse(JToken token)
    {
        if (token is JObject obj)
        {
            var opToken = obj["operator"];
            if (opToken is not null)
            {
                var opStr = (opToken.Type == JTokenType.String ? opToken.Value<string>() : opToken.ToString()) ?? string.Empty;
                if (string.Equals(opStr, "And", StringComparison.OrdinalIgnoreCase) || string.Equals(opStr, "Or", StringComparison.OrdinalIgnoreCase))
                {
                    var opsEl = obj["operands"] as JArray;
                    if (opsEl is null) return null;
                    var children = opsEl.Select(Parse).Where(c => c is not null)!.Cast<VectorFilter>().ToArray();
                    return string.Equals(opStr, "And", StringComparison.OrdinalIgnoreCase) ? new VectorFilterAnd(children) : new VectorFilterOr(children);
                }
                if (string.Equals(opStr, "Not", StringComparison.OrdinalIgnoreCase))
                {
                    var opsEl = obj["operands"] as JArray;
                    if (opsEl is null) return null;
                    var first = opsEl.Select(Parse).FirstOrDefault();
                    return first is null ? null : new VectorFilterNot(first);
                }
                var path = ReadPath(obj);
                if (path is null) return null;
                var valEl = obj["value"];
                if (valEl is null) return null;
                var (value, _) = ReadValue(valEl);
                var op = NormalizeCompare(opStr);
                return new VectorFilterCompare(path, op, value);
            }
            // equality-map shorthand
            var parts = new List<VectorFilter>();
            foreach (var prop in obj.Properties())
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

    private static (object? value, bool ok) ReadValue(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.String: return (value.Value<string>(), true);
            case JTokenType.Boolean: return (value.Value<bool>(), true);
            case JTokenType.Integer:
                {
                    var l = value.Value<long>();
                    return (l, true);
                }
            case JTokenType.Float:
                {
                    var d = value.Value<double>();
                    return (d, true);
                }
            case JTokenType.Null:
            case JTokenType.Undefined:
                return (null, true);
            default: return (null, false);
        }
    }

    private static string[]? ReadPath(JObject el)
    {
        if (!el.TryGetValue("path", out var p)) return null;
        if (p.Type == JTokenType.String) return new[] { p.Value<string>()! };
        if (p.Type == JTokenType.Array) return p.Values<string>().Select(x => x ?? string.Empty).ToArray();
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
