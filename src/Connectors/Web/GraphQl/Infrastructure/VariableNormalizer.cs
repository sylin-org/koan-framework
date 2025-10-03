using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Connector.GraphQl.Infrastructure;

internal static class VariableNormalizer
{
    public static IReadOnlyDictionary<string, object?>? ToDict(object? vars)
    {
        if (vars is null) return null;
        if (vars is IReadOnlyDictionary<string, object?> d) return d;
        if (vars is IDictionary<string, object?> d2) return new Dictionary<string, object?>(d2);
        try
        {
            if (vars is JObject jobj)
            {
                var obj = FromJToken(jobj) as Dictionary<string, object?>;
                return obj is null ? null : FilterNulls(obj);
            }
            if (vars is IDictionary<string, JToken> djt)
            {
                var tmp = new Dictionary<string, object?>();
                foreach (var kv in djt)
                {
                    tmp[kv.Key] = FromJToken(kv.Value);
                }
                return FilterNulls(tmp);
            }
            // Fallback: serialize with Newtonsoft and parse as JObject
            var json = JsonConvert.SerializeObject(vars);
            var parsed = JObject.Parse(json);
            var dict = FromJToken(parsed) as Dictionary<string, object?>;
            return dict is null ? null : FilterNulls(dict);
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

    private static object? FromJToken(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return null;
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Integer:
                {
                    // Prefer smallest fitting integer type
                    var l = token.Value<long>();
                    if (l <= int.MaxValue && l >= int.MinValue) return (int)l;
                    return l;
                }
            case JTokenType.Float:
                {
                    var d = token.Value<double>();
                    // If it can be represented as decimal, prefer decimal for precision
                    return (decimal)d == (decimal)d ? (object)(decimal)d : d;
                }
            case JTokenType.Boolean:
                return token.Value<bool>();
            case JTokenType.Array:
                {
                    var list = new List<object?>();
                    foreach (var item in (JArray)token) list.Add(FromJToken(item));
                    if (list.Count == 0) return null;
                    if (list.Count == 1) return list[0];
                    return list;
                }
            case JTokenType.Object:
                {
                    var obj = (JObject)token;
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in obj.Properties())
                    {
                        dict[prop.Name] = FromJToken(prop.Value);
                    }
                    return dict;
                }
            default:
                return token.ToString();
        }
    }
}

