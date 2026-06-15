using System;
using Koan.Core.Logging;
using Koan.Data.Core.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Core;

/// <summary>
/// Helper to resolve connection strings for adapters from sources.
/// </summary>
public static class AdapterConnectionResolver
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For(typeof(AdapterConnectionResolver));

    /// <summary>
    /// Resolve connection string for adapter and source combination.
    ///
    /// Priority:
    /// 1. <see cref="ConfigurationConstants.Sources"/>.ConnectionString(source, providerId)
    /// 2. ConnectionStrings:{source}
    /// 3. <see cref="ConfigurationConstants.Adapter"/>.ConnectionString(providerId)
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
        var sourceSpecific = config[ConfigurationConstants.Sources.ConnectionString(source, providerId)];
        if (!string.IsNullOrWhiteSpace(sourceSpecific))
            return sourceSpecific;

        // Priority 3: ConnectionStrings:{source}
        var connStr = config.GetConnectionString(source);
        if (!string.IsNullOrWhiteSpace(connStr))
            return connStr;

        // Priority 4: Adapter defaults (Koan:Data:{providerId}:ConnectionString)
        var adapterDefault = config[ConfigurationConstants.Adapter.ConnectionString(providerId)];
        if (!string.IsNullOrWhiteSpace(adapterDefault))
            return adapterDefault;

        // Priority 5: Fallback to Default source if not already trying it
        if (!string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveConnectionString(config, sourceRegistry, providerId, "Default");
        }

        throw new InvalidOperationException(
            $"No connection string found for provider '{providerId}', source '{source}'. " +
            $"Configure one of: {ConfigurationConstants.Sources.ConnectionString(source, providerId)}, " +
            $"ConnectionStrings:{source}, or {ConfigurationConstants.Adapter.ConnectionString(providerId)}");
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
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                // Malformed value in the source definition: warn (do not silently
                // accept), then fall through to config resolution.
                Log.ConfigWarning("adapter-setting.coerce", "malformed-value",
                    ("provider", providerId), ("source", source), ("setting", settingKey),
                    ("origin", "source-definition"), ("targetType", typeof(T).Name),
                    ("value", value), ("reason", ex.Message));
            }
        }

        // Try source-specific config
        var sourceKey = ConfigurationConstants.Sources.Setting(source, providerId, settingKey);
        var sourceValue = config[sourceKey];
        if (!string.IsNullOrWhiteSpace(sourceValue))
        {
            try
            {
                return (T)Convert.ChangeType(sourceValue, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                // Malformed value at the source-specific config key (likely a typo'd
                // setting): warn, then fall through to the adapter default.
                Log.ConfigWarning("adapter-setting.coerce", "malformed-value",
                    ("provider", providerId), ("source", source), ("setting", settingKey),
                    ("origin", "config-source"), ("key", sourceKey), ("targetType", typeof(T).Name),
                    ("value", sourceValue), ("reason", ex.Message));
            }
        }

        // Try adapter default
        var adapterKey = ConfigurationConstants.Adapter.Setting(providerId, settingKey);
        var adapterValue = config[adapterKey];
        if (!string.IsNullOrWhiteSpace(adapterValue))
        {
            try
            {
                return (T)Convert.ChangeType(adapterValue, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                // Malformed value at the adapter default config key (likely a typo'd
                // setting): warn, then fall through to the supplied default value.
                Log.ConfigWarning("adapter-setting.coerce", "malformed-value",
                    ("provider", providerId), ("source", source), ("setting", settingKey),
                    ("origin", "config-adapter"), ("key", adapterKey), ("targetType", typeof(T).Name),
                    ("value", adapterValue), ("reason", ex.Message));
            }
        }

        return defaultValue;
    }
}
