using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Sora.Core.Json;

/// <summary>
/// Flatten/expand JSON to/from a normalized dictionary of dotted paths.
/// - Arrays: path[index]
/// - Escape dot in keys using \u002e (literal dot allowed inside key by replacing on expand/flatten)
/// </summary>
public static class JsonPathMapper
{
    private static readonly Regex Indexer = new("^(.*)\\[(\\d+)\\]$", RegexOptions.Compiled);

    public static IDictionary<string, JToken?> Flatten(JToken token)
    {
        var dict = new Dictionary<string, JToken?>();
        Recurse(token, prefix: null, dict);
        return dict;
    }

    private static void Recurse(JToken token, string? prefix, IDictionary<string, JToken?> dict)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                foreach (var prop in ((JObject)token).Properties())
                {
                    var key = Escape(prop.Name);
                    var path = string.IsNullOrEmpty(prefix) ? key : prefix + "." + key;
                    Recurse(prop.Value, path, dict);
                }
                break;
            case JTokenType.Array:
                var arr = (JArray)token;
                for (int i = 0; i < arr.Count; i++)
                {
                    var path = (prefix ?? string.Empty) + "[" + i + "]";
                    Recurse(arr[i], path, dict);
                }
                break;
            default:
                dict[prefix ?? string.Empty] = ((JValue)token).DeepClone();
                break;
        }
    }

    public static JToken Expand(IDictionary<string, JToken?> map)
    {
        var root = new JObject();
        foreach (var kvp in map)
        {
            Apply(root, kvp.Key, kvp.Value);
        }
        return root;
    }

    private static void Apply(JObject root, string path, JToken? value)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JContainer current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var m = Indexer.Match(seg);
            if (m.Success)
            {
                var name = Unescape(m.Groups[1].Value);
                var index = int.Parse(m.Groups[2].Value);
                var obj = current as JObject ?? throw new InvalidOperationException("Invalid path container");
                if (obj[name] is not JArray arr)
                {
                    arr = new JArray(); obj[name] = arr;
                }
                EnsureSize(arr, index + 1);
                if (i == segments.Length - 1)
                {
                    arr[index] = value ?? JValue.CreateNull();
                    return;
                }
                if (arr[index] is not JObject nextObj)
                {
                    nextObj = new JObject();
                    arr[index] = nextObj;
                }
                current = nextObj;
                continue;
            }

            // plain segment (object)
            var key = Unescape(seg);
            var asObj = current as JObject ?? throw new InvalidOperationException("Invalid path container");
            if (i == segments.Length - 1)
            {
                asObj[key] = value ?? JValue.CreateNull();
                return;
            }
            if (asObj[key] is not JObject child)
            {
                child = new JObject();
                asObj[key] = child;
            }
            current = child;
        }
    }

    private static void EnsureSize(JArray arr, int size)
    {
        while (arr.Count < size) arr.Add(JValue.CreateNull());
    }

    private static string Escape(string key) => key.Replace(".", "\\u002e");
    private static string Unescape(string key) => key.Replace("\\u002e", ".");
}
