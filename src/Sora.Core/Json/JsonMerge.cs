using Newtonsoft.Json.Linq;

namespace Sora.Core.Json;

public static class JsonMerge
{
    public sealed class JsonMergeOptions
    {
        public ArrayMergeStrategy ArrayStrategy { get; init; } = ArrayMergeStrategy.Union;
        /// <summary>
        /// Optional property name to treat arrays of objects as sets keyed by this property. Only applies when ArrayStrategy is Union.
        /// When provided, merges strong/weak arrays by key: strong wins on conflicts; preserves order from strong and appends unseen from weak.
        /// </summary>
        public string? ArrayObjectKey { get; init; }
    }

    /// <summary>
    /// Merge layers in order of strength: earlier = stronger. Stronger values win on conflicts.
    /// Arrays follow the provided strategy (default Union-by-index).
    /// </summary>
    public static JToken Merge(ArrayMergeStrategy arrayStrategy = ArrayMergeStrategy.Union, params string[] layers)
    {
        var options = new JsonMergeOptions { ArrayStrategy = arrayStrategy };
        return Merge(options, layers);
    }

    /// <summary>
    /// Merge with options (non-breaking overload). Earlier layers are stronger. Default behavior matches previous Union behavior.
    /// </summary>
    public static JToken Merge(JsonMergeOptions options, params string[] layers)
    {
        JToken? acc = null;
        for (int i = 0; i < layers.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(layers[i])) continue;
            var token = JToken.Parse(layers[i]);
            acc = acc is null ? token : MergeTokens(acc, token, options);
        }
        return acc ?? new JObject();
    }

    private static JToken MergeTokens(JToken strong, JToken weak, JsonMergeOptions options)
    {
        // If types differ, strong wins for whole node
        if (strong.Type != weak.Type)
        {
            return strong.DeepClone();
        }

        return strong.Type switch
        {
            JTokenType.Object => MergeObjects((JObject)strong, (JObject)weak, options),
            JTokenType.Array  => MergeArrays((JArray)strong, (JArray)weak, options),
            _ => strong.DeepClone()
        };
    }

    private static JObject MergeObjects(JObject strong, JObject weak, JsonMergeOptions options)
    {
        var result = new JObject();
        // keys from both; value from strong if exists, else merge
        var names = strong.Properties().Select(p => p.Name).Union(weak.Properties().Select(p => p.Name));
        foreach (var name in names)
        {
            var s = strong[name];
            var w = weak[name];
            if (s is null && w is not null)
            {
                result[name] = w.DeepClone();
            }
            else if (s is not null && w is null)
            {
                result[name] = s.DeepClone();
            }
            else if (s is not null && w is not null)
            {
                result[name] = MergeTokens(s, w, options);
            }
        }
        return result;
    }

    private static JArray MergeArrays(JArray strong, JArray weak, JsonMergeOptions options)
    {
        if (options.ArrayStrategy == ArrayMergeStrategy.Replace)
            return (JArray)strong.DeepClone();
        if (options.ArrayStrategy == ArrayMergeStrategy.Concat)
            return new JArray(strong.Children().Concat(weak.Children()).Select(c => c.DeepClone()));

        // Union
        if (!string.IsNullOrWhiteSpace(options.ArrayObjectKey)
            && AllObjectsWithKey(strong, options.ArrayObjectKey!)
            && AllObjectsWithKey(weak, options.ArrayObjectKey!))
        {
            return UnionByKey(strong, weak, options.ArrayObjectKey!, options);
        }
        return UnionByIndex(strong, weak, options);
    }

    private static bool AllObjectsWithKey(JArray arr, string key)
    {
        foreach (var t in arr)
        {
            if (t is not JObject o) return false;
            if (o[key] is null) return false;
        }
        return true;
    }

    private static JArray UnionByIndex(JArray strong, JArray weak, JsonMergeOptions options)
    {
        var max = Math.Max(strong.Count, weak.Count);
        var result = new JArray();
        for (int i = 0; i < max; i++)
        {
            var s = i < strong.Count ? strong[i] : null;
            var w = i < weak.Count ? weak[i] : null;
            if (s is null && w is null) continue;
            if (s is null) { result.Add(w!.DeepClone()); continue; }
            if (w is null) { result.Add(s.DeepClone()); continue; }
            result.Add(MergeTokens(s, w, options));
        }
        return result;
    }

    private static JArray UnionByKey(JArray strong, JArray weak, string key, JsonMergeOptions options)
    {
        // Build map from weak by key for quick lookup
        var weakMap = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var jt in weak)
        {
            var obj = (JObject)jt;
            var k = obj[key]!.ToString();
            weakMap[k] = obj;
        }

        var result = new JArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Preserve order from strong; merge with weak on same key
        foreach (var jt in strong)
        {
            var sObj = (JObject)jt;
            var k = sObj[key]!.ToString();
            if (weakMap.TryGetValue(k, out var wObj))
            {
                result.Add(MergeObjects(sObj, wObj, options));
            }
            else
            {
                result.Add((JObject)sObj.DeepClone());
            }
            seen.Add(k);
        }

        // Append remaining from weak not present in strong
        foreach (var jt in weak)
        {
            var wObj = (JObject)jt;
            var k = wObj[key]!.ToString();
            if (!seen.Contains(k))
            {
                result.Add((JObject)wObj.DeepClone());
            }
        }
        return result;
    }
}
