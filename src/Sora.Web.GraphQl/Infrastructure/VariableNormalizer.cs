using System.Text.Json;

namespace Sora.Web.GraphQl.Infrastructure;

internal static class VariableNormalizer
{
    public static IReadOnlyDictionary<string, object?>? ToDict(object? vars)
    {
        if (vars is null) return null;
        if (vars is IReadOnlyDictionary<string, object?> d) return d;
        if (vars is IDictionary<string, object?> d2) return new Dictionary<string, object?>(d2);
        try
        {
            if (vars is JsonElement je)
            {
                if (je.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(je)!);
            }
            if (vars is JsonDocument jd)
            {
                if (jd.RootElement.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(jd.RootElement)!);
            }
            if (vars is System.Text.Json.Nodes.JsonObject jobj)
            {
                using var jdoc = JsonDocument.Parse(jobj.ToJsonString());
                if (jdoc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(jdoc.RootElement)!);
            }
            if (vars is IDictionary<string, JsonElement> dje)
            {
                var tmp = new Dictionary<string, object?>();
                foreach (var kv in dje)
                {
                    tmp[kv.Key] = FromJsonElement(kv.Value);
                }
                return FilterNulls(tmp);
            }
            var json = JsonSerializer.Serialize(vars);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return FilterNulls((Dictionary<string, object?>)FromJsonElement(doc.RootElement)!);
        }
        catch { return null; }
    }

    private static IReadOnlyDictionary<string, object?> FilterNulls(Dictionary<string, object?> dict)
    {
        if (dict.Count == 0) return dict;
        var filtered = new Dictionary<string, object?>();
        foreach (var (k, v) in dict)
        {
            if (v is null) continue;
            if (v is IEnumerable<object?> seq && v is not string)
            {
                var arr = (seq as IList<object?>) ?? seq.ToList();
                if (arr.Count == 0) continue;
            }
            filtered[k] = v;
        }
        return filtered;
    }

    private static object? FromJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt32(out var i)) return i;
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetDecimal();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetBoolean();
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray()) list.Add(FromJsonElement(item));
                if (list.Count == 0) return null;
                if (list.Count == 1) return list[0];
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject()) dict[prop.Name] = FromJsonElement(prop.Value);
                return dict;
            default:
                return null;
        }
    }
}
