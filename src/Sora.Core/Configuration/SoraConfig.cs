using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Sora.Core.Configuration;

// Simple, no-caching configuration helper
public static class SoraConfig
{
    // Generic read with default(T)
    public static T? Read<T>(IConfiguration? cfg, string key)
        => Read(cfg, key, default(T)!);

    // Boolean convenience overloads (no cfg)
    public static bool Read(string key) => Read<bool>(null, key, default);
    public static bool Read(string key, bool defaultValue) => Read<bool>(null, key, defaultValue);

    // Boolean convenience overloads (with cfg)
    public static bool Read(IConfiguration cfg, string key) => Read<bool>(cfg, key, default);
    public static bool Read(IConfiguration cfg, string key, bool defaultValue) => Read<bool>(cfg, key, defaultValue);

    // Read with explicit default
    public static T Read<T>(IConfiguration? cfg, string key, T defaultValue)
    {
        // 1) env var override (derived from key: ":" -> "__")
        var envName = DeriveEnvVarName(key);
        var envVal = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(envVal) && TryConvert(envVal!, out T parsed))
            return parsed;

        // 2) IConfiguration
        if (cfg is not null)
        {
            // IConfiguration can bind directly when T is simple; prefer strong GetValue
            var val = cfg.GetValue<T?>(key);
            if (val is not null)
                return val;

            var str = cfg[key];
            if (!string.IsNullOrWhiteSpace(str) && TryConvert(str!, out parsed))
                return parsed;
        }

        // 3) default
        return defaultValue;
    }

    private static string DeriveEnvVarName(string key) => key.Replace(":", "__");

    private static bool TryConvert<T>(string value, out T result)
    {
        try
        {
            object? boxed;
            var target = typeof(T);

            if (target == typeof(string))
            {
                boxed = value;
            }
            else if (target == typeof(bool) || target == typeof(bool?))
            {
                if (TryParseBool(value, out var b)) { boxed = b; }
                else { boxed = default(bool); }
            }
            else if (target.IsEnum)
            {
                boxed = Enum.Parse(target, value, ignoreCase: true);
            }
            else if (target == typeof(int) || target == typeof(int?))
            {
                boxed = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : default(int);
            }
            else if (target == typeof(double) || target == typeof(double?))
            {
                boxed = double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d) ? d : default(double);
            }
            else if (target == typeof(TimeSpan) || target == typeof(TimeSpan?))
            {
                boxed = TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts) ? ts : default(TimeSpan);
            }
            else
            {
                // Fallback for other primitives
                boxed = (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(target) ?? target, CultureInfo.InvariantCulture);
            }

            result = (T)boxed!;
            return true;
        }
        catch
        {
            result = default!;
            return false;
        }
    }

    private static bool TryParseBool(string s, out bool value)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "y":
            case "on":
                value = true; return true;
            case "0":
            case "false":
            case "no":
            case "n":
            case "off":
                value = false; return true;
            default:
                return bool.TryParse(s, out value);
        }
    }
}
