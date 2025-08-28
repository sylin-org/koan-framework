using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sora.Core.Json;

public static class JsonExtensions
{
    public static string ToJson(this object? value) => JsonConvert.SerializeObject(value, JsonDefaults.Settings);

    public static T? FromJson<T>(this string json) => JsonConvert.DeserializeObject<T>(json, JsonDefaults.Settings);

    public static bool TryFromJson<T>(this string json, out T? value)
    {
        try
        {
            value = JsonConvert.DeserializeObject<T>(json, JsonDefaults.Settings);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    public static object? FromJson(this string json, Type type)
        => JsonConvert.DeserializeObject(json, type, JsonDefaults.Settings);

    public static bool TryFromJson(this string json, Type type, out object? value)
    {
        try
        {
            value = JsonConvert.DeserializeObject(json, type, JsonDefaults.Settings);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Produce a canonical JSON string: objects have properties sorted by name, no whitespace.
    /// Arrays keep their original order; values are recursively normalized.
    /// </summary>
    public static string ToCanonicalJson(this string json)
    {
        var token = JToken.Parse(json);
        var normalized = Normalize(token);
        return normalized.ToString(Newtonsoft.Json.Formatting.None);
    }

    public static string ToCanonicalJson(this JToken token)
    {
        var normalized = Normalize(token);
        return normalized.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JToken Normalize(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var obj = (JObject)token;
                var props = obj.Properties()
                    // Normalize values first so type checks are consistent
                    .Select(p => new JProperty(p.Name, Normalize(p.Value)))
                    // Place array-valued properties after non-array ones; then sort by name
                    .OrderBy(p => p.Value.Type == JTokenType.Array ? 1 : 0)
                    .ThenBy(p => p.Name, StringComparer.Ordinal);
                var sorted = new JObject();
                foreach (var p in props) sorted.Add(p);
                return sorted;
            case JTokenType.Array:
                var arr = (JArray)token;
                var normArr = new JArray();
                foreach (var item in arr) normArr.Add(Normalize(item));
                return normArr;
            default:
                return ((JValue)token).DeepClone();
        }
    }
}
