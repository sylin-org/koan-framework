using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core;

/// <summary>
/// Registry for data sources discovered from configuration or registered programmatically.
///
/// Sources are named configurations that define adapter, connection string, and adapter-specific settings.
/// Discovered from "Koan:Data:Sources:{name}" configuration sections.
///
/// Example configuration:
/// {
///   "Koan": {
///     "Data": {
///       "Sources": {
///         "Analytics": {
///           "Adapter": "sqlserver",
///           "ConnectionString": "Server=analytics-db;...",
///           "MaxPageSize": "500"
///         }
///       }
///     }
///   }
/// }
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly ConcurrentDictionary<string, SourceDefinition> _sources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Definition of a data source with adapter and settings.
    /// </summary>
    /// <param name="Name">Source name (e.g., "Analytics", "Backup")</param>
    /// <param name="Adapter">Adapter identifier (e.g., "sqlserver", "mongodb")</param>
    /// <param name="ConnectionString">Connection string for this source</param>
    /// <param name="Settings">Adapter-specific settings (e.g., MaxPageSize, CommandTimeout)</param>
    public record SourceDefinition(
        string Name,
        string Adapter,
        string ConnectionString,
        IReadOnlyDictionary<string, string> Settings);

    /// <summary>
    /// Auto-discover sources from IConfiguration at "Koan:Data:Sources:{name}".
    /// Always ensures "Default" source exists (even if empty).
    /// </summary>
    /// <param name="config">Configuration to scan for sources</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        var sourcesSection = config.GetSection("Koan:Data:Sources");

        foreach (var sourceConfig in sourcesSection.GetChildren())
        {
            var sourceName = sourceConfig.Key;
            var adapter = sourceConfig["Adapter"];
            var connectionString = sourceConfig["ConnectionString"] ?? "";

            // Skip sources without explicit adapter (unless it's Default)
            if (string.IsNullOrWhiteSpace(adapter) &&
                !string.Equals(sourceName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogWarning(
                    "Source '{SourceName}' has no adapter configured, skipping auto-discovery",
                    sourceName);
                continue;
            }

            // Extract all settings except Adapter and ConnectionString
            var settings = sourceConfig.GetChildren()
                .Where(c => c.Key != "Adapter" && c.Key != "ConnectionString")
                .ToDictionary(
                    c => c.Key,
                    c => c.Value ?? "",
                    StringComparer.OrdinalIgnoreCase);

            RegisterSource(new SourceDefinition(
                sourceName,
                adapter ?? "",
                connectionString,
                settings));

            logger?.LogDebug(
                "Discovered source '{SourceName}' with adapter '{Adapter}'",
                sourceName,
                adapter ?? "(none)");
        }

        // Always ensure "Default" source exists (may have empty adapter → resolved by priority)
        if (!_sources.ContainsKey("Default"))
        {
            RegisterSource(new SourceDefinition(
                "Default",
                "",
                "",
                new Dictionary<string, string>()));

            logger?.LogDebug("Created implicit 'Default' source with no adapter (uses priority resolution)");
        }
    }

    /// <summary>
    /// Programmatically register a source (for runtime/testing scenarios).
    /// </summary>
    /// <param name="source">Source definition to register</param>
    /// <exception cref="ArgumentException">Thrown when source name is empty</exception>
    public void RegisterSource(SourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Source name cannot be empty", nameof(source));

        _sources[source.Name] = source;
    }

    /// <summary>
    /// Get source definition by name (case-insensitive).
    /// </summary>
    /// <param name="name">Source name</param>
    /// <returns>Source definition or null if not found</returns>
    public SourceDefinition? GetSource(string name)
        => _sources.TryGetValue(name, out var source) ? source : null;

    /// <summary>
    /// Try to get source definition by name (case-insensitive).
    /// </summary>
    /// <param name="name">Source name</param>
    /// <param name="source">Source definition if found</param>
    /// <returns>True if source exists, false otherwise</returns>
    public bool TryGetSource(string name, out SourceDefinition source)
        => _sources.TryGetValue(name, out source!);

    /// <summary>
    /// Get all registered source names.
    /// </summary>
    public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();

    /// <summary>
    /// Check if source exists (case-insensitive).
    /// </summary>
    public bool HasSource(string name) => _sources.ContainsKey(name);
}
