using System.Reflection;

namespace Sora.Messaging;

// Utilities to derive transport-agnostic message metadata from POCOs and attributes
public static class MessageMeta
{
    public static IDictionary<string, object> ExtractHeaders(Type type, object message)
    {
        var dict = new Dictionary<string, object>();
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var h = p.GetCustomAttribute<HeaderAttribute>(inherit: true);
            if (h is null) continue;
            // Do not promote sensitive properties
            var isSensitive = p.GetCustomAttribute<SensitiveAttribute>(inherit: true) != null;
            if (isSensitive) continue;
            var val = p.GetValue(message);
            if (val is null) continue;
            dict[h.Name] = val.ToString() ?? string.Empty;
        }
        return dict;
    }

    public static int ResolveDelaySeconds(Type type, object message)
    {
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var d = p.GetCustomAttribute<DelaySecondsAttribute>(inherit: true);
            if (d is null) continue;
            var val = p.GetValue(message);
            if (val is null) continue;
            if (int.TryParse(val.ToString(), out var seconds) && seconds > 0) return seconds;
        }
        return 0;
    }

    public static string? ResolveIdempotencyKey(Type type, object message)
    {
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attr = p.GetCustomAttribute<IdempotencyKeyAttribute>(inherit: true);
            if (attr is null) continue;
            var val = p.GetValue(message);
            var s = val?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    public static string ResolvePartitionSuffix(Type type, object message, int shards = 16)
    {
        var partProp = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => p.GetCustomAttribute<PartitionKeyAttribute>(inherit: true) != null);
        if (partProp is null) return string.Empty;
        var value = partProp.GetValue(message)?.ToString();
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var mod = Math.Max(1, shards);
        var hash = Math.Abs(value.GetHashCode()) % mod;
        return $".p{hash}";
    }
}
