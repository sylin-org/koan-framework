using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions.Vector.Filtering;

public static class VectorFilterJson
{
    /// <summary>
    /// Parse a vector metadata filter, distinguishing "no filter supplied" from "filter supplied
    /// but invalid" (DATA-0097 F1). Returns null ONLY for a genuinely absent filter (null input);
    /// throws <see cref="FilterParseException"/> when a filter was supplied but is malformed or
    /// uses an unsupported operator/shape. Callers must NOT treat a thrown filter as "no filter"
    /// (that is the silent-match-all data hazard this replaces).
    /// </summary>
    public static VectorFilter? ParseOrThrow(object? input)
    {
        if (input is null) return null;                 // genuinely no filter -> full kNN
        if (input is VectorFilter vf) return vf;

        JToken token;
        try
        {
            token = input switch
            {
                JToken jt => jt,
                string s => JToken.Parse(s),
                _ => JToken.FromObject(input)
            };
        }
        catch (Exception ex)
        {
            throw new FilterParseException($"Invalid vector filter JSON: {ex.Message}", ex);
        }

        var parsed = Parse(token);
        if (parsed is null)
            throw new FilterParseException("Vector filter was supplied but could not be parsed into a valid filter.");
        return parsed;
    }

    /// <summary>
    /// Back-compat bool wrapper. <paramref name="filter"/> is non-null on success. NOTE: a return
    /// of false now means BOTH "no filter" and "parse failed" only at the boundary where the caller
    /// has already decided to tolerate both; prefer <see cref="ParseOrThrow"/> on read paths so an
    /// invalid filter fails loud instead of silently widening the result set.
    /// </summary>
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
                var opStr = (opToken.Type == JTokenType.String ? opToken.Value<string>() : opToken.ToString()) ?? "";
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
                var op = NormalizeCompare(opStr);
                // F3: In/Between carry an array RHS; scalar operators carry a scalar. Read the
                // shape the operator requires rather than silently dropping arrays.
                if (op is VectorFilterOperator.In or VectorFilterOperator.Between)
                {
                    var arr = ReadArray(valEl);
                    if (arr is null)
                        throw new FilterParseException($"Vector filter operator '{op}' requires an array value.");
                    return new VectorFilterCompare(path, op, arr);
                }
                var (value, ok) = ReadValue(valEl);
                if (!ok)
                    throw new FilterParseException($"Vector filter value for operator '{op}' on '{string.Join('.', path)}' is not a supported scalar.");
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

    private static string Escape(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static (object? value, bool ok) ReadValue(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.String: return (value.Value<string>(), true);
            case JTokenType.Boolean: return (value.Value<bool>(), true);
            case JTokenType.Integer: return (value.Value<long>(), true);
            case JTokenType.Float: return (value.Value<double>(), true);
            case JTokenType.Null:
            case JTokenType.Undefined:
                return (null, true);
            default: return (null, false);
        }
    }

    /// <summary>F3: reads a JSON array RHS into a list of scalars for In/Between. Null if not an array.</summary>
    private static IReadOnlyList<object?>? ReadArray(JToken value)
    {
        if (value is not JArray arr) return null;
        var items = new List<object?>(arr.Count);
        foreach (var el in arr)
        {
            var (v, ok) = ReadValue(el);
            if (!ok) throw new FilterParseException("Vector filter array contains a non-scalar element.");
            items.Add(v);
        }
        return items;
    }

    private static string[]? ReadPath(JObject el)
    {
        if (!el.TryGetValue("path", out var p)) return null;
        if (p.Type == JTokenType.String) return new[] { p.Value<string>()! };
        if (p.Type == JTokenType.Array) return p.Values<string>().Select(x => x ?? "").ToArray();
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
            "in" => VectorFilterOperator.In,
            "between" => VectorFilterOperator.Between,
            // F2 (parser half): an unrecognized operator is a real error, not silently Eq.
            _ => Enum.TryParse<VectorFilterOperator>(op, ignoreCase: true, out var e)
                ? e
                : throw new FilterParseException($"Unknown vector filter operator '{op}'.")
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
                return ("\"valueInt\"", Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            case float or double or decimal:
                return ("\"valueNumber\"", Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            default:
                return ("\"valueText\"", $"\"{Escape(value.ToString())}\"");
        }
    }
}
