using System;
using Koan.Core.Logging;
using Koan.Data.Abstractions;
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
    /// 1. The source definition's generic connection, when the source is unowned or belongs to this provider.
    /// 2. <see cref="ConfigurationConstants.Sources"/>.ConnectionString(source, providerId).
    /// 3. ConnectionStrings:{source}, under the same source-ownership rule as priority 1.
    /// 4. <see cref="ConfigurationConstants.Adapter"/>.ConnectionString(providerId).
    /// 5. Fallback to the "Default" source when the requested source is not Default.
    /// </summary>
    public static string ResolveConnectionString(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source)
        => ResolveConnectionStringCore(config, sourceRegistry, providerId, source, null);

    /// <summary>
    /// Resolve a connection while enforcing ownership of generic source declarations. The factory's declarative
    /// provider identity and aliases remain the single ownership source.
    /// </summary>
    public static string ResolveConnectionString(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        IAdapterFactory? sourceOwner)
        => ResolveConnectionStringCore(config, sourceRegistry, providerId, source, sourceOwner);

    private static string ResolveConnectionStringCore(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        IAdapterFactory? sourceOwner)
    {
        var configured = TryResolveConfiguredConnection(
            config, sourceRegistry, providerId, source, sourceOwner);
        if (configured is not null) return configured;

        // Priority 5: Fallback to Default source if not already trying it
        if (!string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveConnectionStringCore(
                config, sourceRegistry, providerId, "Default", sourceOwner);
        }

        throw new InvalidOperationException(
            $"No connection string found for provider '{providerId}', source '{source}'. " +
            $"Configure one of: {ConfigurationConstants.Sources.ConnectionString(source, providerId)}, " +
            $"ConnectionStrings:{source}, or {ConfigurationConstants.Adapter.ConnectionString(providerId)}");
    }

    /// <summary>
    /// Resolve the per-source connection string for a Database-mode routed source, collapsing the <c>"auto"</c> /
    /// blank discovery sentinel onto the already-resolved Default connection (ARCH-0103 P5 fleet hoist). The flow:
    /// <list type="number">
    /// <item>The Default source (or blank) first honors a concrete configured connection. An absent, blank, or
    /// <c>"auto"</c> source falls back to <paramref name="resolvedDefault"/> — the physical connection the adapter's
    /// options configurator already discovered.</item>
    /// <item>A non-Default source resolves via <c>ResolveConnectionString</c>; if that yields the literal
    /// <c>"auto"</c> or blank — a source relying on runtime discovery, which this resolver cannot perform — it collapses
    /// onto <paramref name="resolvedDefault"/> so the per-source pool/store never keys on the unresolved sentinel.</item>
    /// </list>
    /// This is the shared form of the per-adapter <c>ResolveRoutedConnection</c> the Mongo (<c>d0ea898e</c>) and Couchbase
    /// (ARCH-0103 P4) factories grew locally: the relational + SqliteVec factories had no <c>"auto"</c> fallback at all,
    /// so a non-Default source relying on discovery keyed its store on <c>"auto"</c>. With this they collapse uniformly.
    /// </summary>
    public static string ResolveRoutedConnection(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        string? resolvedDefault)
        => ResolveRoutedConnection(
            config, sourceRegistry, providerId, source, resolvedDefault, null);

    /// <summary>
    /// Source-aware routed resolution for factories that can identify their provider aliases. A generic source
    /// connection is consumed only when that source is unowned or <paramref name="sourceOwner"/> confirms the
    /// configured adapter belongs to this provider; provider-scoped source configuration remains available.
    /// </summary>
    public static string ResolveRoutedConnection(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        string? resolvedDefault,
        IAdapterFactory? sourceOwner)
    {
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            const string defaultSource = "Default";
            var defaultConfigured = TryResolveConfiguredConnection(
                config, sourceRegistry, providerId, defaultSource, sourceOwner);

            if (!IsUnresolvedSentinel(defaultConfigured)) return defaultConfigured!;
            if (!string.IsNullOrWhiteSpace(resolvedDefault)) return resolvedDefault!;

            // Preserve the unresolved sentinel when there is no discovery result; otherwise retain the existing
            // fail-loud contract for a completely unconfigured provider.
            return defaultConfigured ?? ResolveConnectionStringCore(
                config, sourceRegistry, providerId, defaultSource, sourceOwner);
        }

        var configured = TryResolveConfiguredConnection(
            config, sourceRegistry, providerId, source, sourceOwner);
        if (!IsUnresolvedSentinel(configured)) return configured!;
        if (!string.IsNullOrWhiteSpace(resolvedDefault)) return resolvedDefault!;

        // Preserve an explicit unresolved intent when no runtime discovery result exists; an entirely absent
        // placement retains the legacy fail-loud path and its actionable configuration error.
        return configured ?? ResolveConnectionStringCore(
            config, sourceRegistry, providerId, source, sourceOwner);
    }

    // Blank or the literal "auto" discovery sentinel — a source whose physical connection the static resolver cannot
    // produce (it relies on runtime discovery, which only the Default source's options configurator performed at boot).
    private static bool IsUnresolvedSentinel(string? connection)
        => string.IsNullOrWhiteSpace(connection) || string.Equals(connection.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    // The shared priorities used by the full connection path. Provider configurators supply only provider-owned
    // configuration/discovery defaults; generic ConnectionStrings:{source} stays here so source ownership is decided
    // once, with the actual registry and the factory's declarative identity.
    private static string? TryResolveConfiguredConnection(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        IAdapterFactory? sourceOwner)
    {
        var sourceDefinition = sourceRegistry.GetSource(source);
        var sourceBelongsToProvider = SourceBelongsToProvider(sourceDefinition, sourceOwner);
        var sourceConnection = TryResolveSourceConnection(
            config, sourceRegistry, providerId, source, sourceOwner);
        if (sourceConnection is not null) return sourceConnection;

        // Priority 3: ConnectionStrings:{source}. This is another generic source declaration, so an adapter must not
        // consume it when the source explicitly belongs to a different provider. Provider-scoped source configuration
        // above remains available for an intentional adapter override.
        if (sourceBelongsToProvider)
        {
            var connectionString = config.GetConnectionString(source);
            if (!string.IsNullOrWhiteSpace(connectionString))
                return connectionString;
        }

        // Priority 4: Adapter defaults (Koan:Data:{providerId}:ConnectionString)
        var adapterDefault = config[ConfigurationConstants.Adapter.ConnectionString(providerId)];
        return string.IsNullOrWhiteSpace(adapterDefault) ? null : adapterDefault;
    }

    private static string? TryResolveSourceConnection(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        IAdapterFactory? sourceOwner)
    {
        // Priority 1: Source-specific connection from source definition
        var sourceDefinition = sourceRegistry.GetSource(source);
        var sourceBelongsToProvider = SourceBelongsToProvider(sourceDefinition, sourceOwner);
        if (sourceBelongsToProvider && !string.IsNullOrWhiteSpace(sourceDefinition?.ConnectionString))
            return sourceDefinition.ConnectionString;

        // Priority 2: Koan:Data:Sources:{source}:{providerId}:ConnectionString
        var sourceSpecific = config[ConfigurationConstants.Sources.ConnectionString(source, providerId)];
        return string.IsNullOrWhiteSpace(sourceSpecific) ? null : sourceSpecific;
    }

    // A missing/implicit source is provider-neutral. The null owner preserves the unqualified overload's behavior;
    // provider factories contribute their declarative identity and aliases without a second predicate authority.
    private static bool SourceBelongsToProvider(
        DataSourceRegistry.SourceDefinition? sourceDefinition,
        IAdapterFactory? sourceOwner)
        => string.IsNullOrWhiteSpace(sourceDefinition?.Adapter) ||
           sourceOwner is null ||
           string.Equals(sourceOwner.Provider, sourceDefinition.Adapter, StringComparison.OrdinalIgnoreCase) ||
           sourceOwner.Aliases.Contains(sourceDefinition.Adapter, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get source-specific setting or fall back to adapter default.
    /// </summary>
    public static T GetSourceSetting<T>(
        IConfiguration config,
        DataSourceRegistry sourceRegistry,
        string providerId,
        string source,
        string settingKey,
        T defaultValue,
        IAdapterFactory? sourceOwner = null)
    {
        // Generic source settings belong to the source's adapter. Provider-scoped settings below remain an explicit
        // override, just like provider-scoped connection strings.
        var sourceDefinition = sourceRegistry.GetSource(source);
        if (SourceBelongsToProvider(sourceDefinition, sourceOwner) &&
            sourceDefinition?.Settings.TryGetValue(settingKey, out var value) == true)
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
