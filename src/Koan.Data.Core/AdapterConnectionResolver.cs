using System;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Core;

/// <summary>
/// Helper to resolve connection strings for adapters from sources.
/// </summary>
internal static class AdapterConnectionResolver
{
    /// <summary>
    /// Resolve connection string for adapter and source combination.
    ///
    /// Priority:
    /// 1. Koan:Data:Sources:{source}:{providerId}:ConnectionString
    /// 2. ConnectionStrings:{source}
    /// 3. Koan:Data:{providerId}:ConnectionString (adapter default)
    /// 4. Fallback to "Default" source if current source != "Default"
    /// </summary>
    public static string ResolveConnectionString(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source)
    {
        // Priority 1: Source-specific connection from source definition
        var sourceDefinition = sourceRegistry.GetSource(source);
        if (sourceDefinition != null && !string.IsNullOrWhiteSpace(sourceDefinition.ConnectionString))
        {
            return sourceDefinition.ConnectionString;
        }

        // Priority 2: Koan:Data:Sources:{source}:{providerId}:ConnectionString
        var sourceSpecific = config[$"Koan:Data:Sources:{source}:{providerId}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(sourceSpecific))
            return sourceSpecific;

        // Priority 3: ConnectionStrings:{source}
        var connStr = config.GetConnectionString(source);
        if (!string.IsNullOrWhiteSpace(connStr))
            return connStr;

        // Priority 4: Adapter defaults (Koan:Data:{providerId}:ConnectionString)
        var adapterDefault = config[$"Koan:Data:{providerId}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(adapterDefault))
            return adapterDefault;

        // Priority 5: Fallback to Default source if not already trying it
        if (!string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveConnectionString(config, sourceRegistry, providerId, "Default");
        }

        throw new InvalidOperationException(
            $"No connection string found for provider '{providerId}', source '{source}'. " +
            $"Configure one of: Koan:Data:Sources:{source}:ConnectionString, " +
            $"ConnectionStrings:{source}, or Koan:Data:{providerId}:ConnectionString");
    }

    /// <summary>
    /// Get source-specific setting or fall back to adapter default.
    /// </summary>
    public static T GetSourceSetting<T>(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        string settingKey,
        T defaultValue)
    {
        // Try source definition first
        var sourceDefinition = sourceRegistry.GetSource(source);
        if (sourceDefinition?.Settings.TryGetValue(settingKey, out var value) == true)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                // Fall through to config resolution
            }
        }

        // Try source-specific config
        var sourceKey = $"Koan:Data:Sources:{source}:{providerId}:{settingKey}";
        var sourceValue = config[sourceKey];
        if (!string.IsNullOrWhiteSpace(sourceValue))
        {
            try
            {
                return (T)Convert.ChangeType(sourceValue, typeof(T));
            }
            catch
            {
                // Fall through to adapter default
            }
        }

        // Try adapter default
        var adapterKey = $"Koan:Data:{providerId}:{settingKey}";
        var adapterValue = config[adapterKey];
        if (!string.IsNullOrWhiteSpace(adapterValue))
        {
            try
            {
                return (T)Convert.ChangeType(adapterValue, typeof(T));
            }
            catch
            {
                // Fall through to default value
            }
        }

        return defaultValue;
    }
}
