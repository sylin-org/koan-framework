using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sora.Flow.Model;

public static class ControlCommandExtensions
{
    public static bool TryGetString(this ControlCommand cmd, string key, out string value)
    {
        value = string.Empty;
        if (cmd.Parameters is null) return false;
        if (!cmd.Parameters.TryGetValue(key, out var elem)) return false;
        if (elem.ValueKind == JsonValueKind.String)
        {
            value = elem.GetString() ?? string.Empty;
            return true;
        }
        return false;
    }

    public static bool TryGetInt32(this ControlCommand cmd, string key, out int value)
    {
        value = default;
        if (cmd.Parameters is null) return false;
        if (!cmd.Parameters.TryGetValue(key, out var elem)) return false;
        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var v))
        {
            value = v;
            return true;
        }
        return false;
    }

    public static bool TryGetBoolean(this ControlCommand cmd, string key, out bool value)
    {
        value = default;
        if (cmd.Parameters is null) return false;
        if (!cmd.Parameters.TryGetValue(key, out var elem)) return false;
        if (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False)
        {
            value = elem.GetBoolean();
            return true;
        }
        return false;
    }

    public static bool TryGetObject<T>(this ControlCommand cmd, string key, out T? value, JsonSerializerOptions? opts = null)
    {
        value = default;
        if (cmd.Parameters is null) return false;
        if (!cmd.Parameters.TryGetValue(key, out var elem)) return false;
        try
        {
            value = elem.Deserialize<T>(opts ?? DefaultJsonOptions);
            return value is not null;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    public static ControlCommand WithParam<T>(this ControlCommand cmd, string key, T value, JsonSerializerOptions? opts = null)
    {
        var json = JsonSerializer.SerializeToElement(value, opts ?? DefaultJsonOptions);
        cmd.Parameters[key] = json;
        return cmd;
    }

    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);
}
