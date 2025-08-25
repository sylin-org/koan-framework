using Microsoft.Extensions.Configuration;

namespace Sora.Core.Extensions;

public static class ConfigurationExtensions
{
    // Generic read with implicit default(T)
    public static T? Read<T>(this IConfiguration? cfg, string key)
        => Configuration.Read<T>(cfg, key);

    // Generic read with explicit default
    public static T Read<T>(this IConfiguration? cfg, string key, T defaultValue)
        => Configuration.Read(cfg, key, defaultValue);

    // First-non-null helpers
    public static string? ReadFirst(this IConfiguration? cfg, params string[] keys)
        => Configuration.ReadFirst(cfg, keys);

    public static string ReadFirst(this IConfiguration? cfg, string defaultValue, params string[] keys)
        => Configuration.ReadFirst(cfg, defaultValue, keys);

    public static bool ReadFirst(this IConfiguration? cfg, bool defaultValue, params string[] keys)
        => Configuration.ReadFirst(cfg, defaultValue, keys);

    public static int ReadFirst(this IConfiguration? cfg, int defaultValue, params string[] keys)
        => Configuration.ReadFirst(cfg, defaultValue, keys);
}
