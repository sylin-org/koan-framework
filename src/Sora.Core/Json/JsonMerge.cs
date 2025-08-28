using Newtonsoft.Json.Linq;

namespace Sora.Core.Json;

public enum ArrayMergeStrategy
{
    Union,
    Replace,
    Concat
}

public static class JsonMerge
{
    /// <summary>
    /// Merge layers in order of strength: earlier = stronger. Stronger values win on conflicts.
    /// Arrays follow the provided strategy (default Union-by-index).
    /// </summary>
    public static JToken Merge(ArrayMergeStrategy arrayStrategy = ArrayMergeStrategy.Union, params string[] layers)
    {
        JToken? acc = null;
        for (int i = 0; i < layers.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(layers[i])) continue;
            var token = JToken.Parse(layers[i]);
            acc = acc is null ? token : MergeTokens(acc, token, arrayStrategy);
        }
        return acc ?? new JObject();
    }

    private static JToken MergeTokens(JToken strong, JToken weak, ArrayMergeStrategy arrays)
    {
        // If types differ, strong wins for whole node
        if (strong.Type != weak.Type)
        {
            return strong.DeepClone();
        }

        return strong.Type switch
        {
            JTokenType.Object => MergeObjects((JObject)strong, (JObject)weak, arrays),
            JTokenType.Array  => MergeArrays((JArray)strong, (JArray)weak, arrays),
            _ => strong.DeepClone()
        };
    }

    private static JObject MergeObjects(JObject strong, JObject weak, ArrayMergeStrategy arrays)
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
                result[name] = MergeTokens(s, w, arrays);
            }
        }
        return result;
    }

    private static JArray MergeArrays(JArray strong, JArray weak, ArrayMergeStrategy arrays)
    {
        return arrays switch
        {
            ArrayMergeStrategy.Replace => (JArray)strong.DeepClone(),
            ArrayMergeStrategy.Concat  => new JArray(strong.Children().Concat(weak.Children()).Select(c => c.DeepClone())),
            _ => UnionByIndex(strong, weak)
        };
    }

    private static JArray UnionByIndex(JArray strong, JArray weak)
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
            result.Add(MergeTokens(s, w, ArrayMergeStrategy.Union));
        }
        return result;
    }
}
