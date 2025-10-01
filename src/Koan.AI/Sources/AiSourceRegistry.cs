using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources;

/// <summary>
/// Registry for AI sources discovered from configuration or registered programmatically.
/// Similar to Data.DataSourceRegistry but for AI providers.
///
/// Sources are discovered from:
/// 1. "Koan:Ai:Sources:{name}" - Explicit source definitions
/// 2. "Koan:Ai:Ollama" - Simple backward-compatible config (creates "Default" source)
/// 3. Auto-discovery - Creates "ollama-auto-*" sources
/// </summary>
public sealed class AiSourceRegistry : IAiSourceRegistry
{
    private readonly ConcurrentDictionary<string, AiSourceDefinition> _sources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Auto-discover sources from IConfiguration at "Koan:Ai:Sources:{name}".
    /// Also handles backward-compatible "Koan:Ai:Ollama" simple config.
    /// Always ensures "Default" source exists (even if empty).
    /// </summary>
    /// <param name="config">Configuration to scan for sources</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        // 1. Discover explicit sources from Koan:Ai:Sources
        DiscoverExplicitSources(config, logger);

        // 2. Handle backward-compatible Koan:Ai:Ollama config
        DiscoverLegacyOllamaConfig(config, logger);

        // 3. Always ensure "Default" source exists (may have empty provider → resolved by priority)
        if (!_sources.ContainsKey("Default"))
        {
            RegisterSource(new AiSourceDefinition
            {
                Name = "Default",
                Provider = "",
                Capabilities = new Dictionary<string, AiCapabilityConfig>(),
                Settings = new Dictionary<string, string>(),
                Origin = "implicit",
                IsAutoDiscovered = false
            });

            logger?.LogDebug("Created implicit 'Default' AI source with no provider (uses priority resolution)");
        }
    }

    private void DiscoverExplicitSources(IConfiguration config, ILogger? logger)
    {
        var sourcesSection = config.GetSection("Koan:Ai:Sources");

        foreach (var sourceConfig in sourcesSection.GetChildren())
        {
            var sourceName = sourceConfig.Key;
            var provider = sourceConfig["Provider"];
            var connectionString = sourceConfig["ConnectionString"];
            var group = sourceConfig["Group"];
            var priority = sourceConfig.GetValue<int?>("Priority") ?? 50;

            // Skip sources without explicit provider (unless it's Default)
            if (string.IsNullOrWhiteSpace(provider) &&
                !string.Equals(sourceName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogWarning(
                    "AI Source '{SourceName}' has no provider configured, skipping auto-discovery",
                    sourceName);
                continue;
            }

            // Parse capabilities
            var capabilities = ParseCapabilities(sourceConfig.GetSection("Capabilities"), logger);

            // Extract all settings except known keys
            var settings = sourceConfig.GetChildren()
                .Where(c => c.Key != "Provider" &&
                           c.Key != "ConnectionString" &&
                           c.Key != "Group" &&
                           c.Key != "Priority" &&
                           c.Key != "Capabilities")
                .ToDictionary(
                    c => c.Key,
                    c => c.Value ?? "",
                    StringComparer.OrdinalIgnoreCase);

            RegisterSource(new AiSourceDefinition
            {
                Name = sourceName,
                Provider = provider ?? "",
                ConnectionString = connectionString,
                Group = group,
                Priority = priority,
                Capabilities = capabilities,
                Settings = settings,
                Origin = "explicit-config",
                IsAutoDiscovered = false
            });

            logger?.LogDebug(
                "Discovered AI source '{SourceName}' with provider '{Provider}' and {CapabilityCount} capabilities",
                sourceName,
                provider ?? "(none)",
                capabilities.Count);
        }
    }

    private void DiscoverLegacyOllamaConfig(IConfiguration config, ILogger? logger)
    {
        var ollamaSection = config.GetSection("Koan:Ai:Ollama");
        if (!ollamaSection.Exists())
        {
            return;
        }

        var defaultModel = ollamaSection["DefaultModel"];
        var baseUrl = ollamaSection["BaseUrl"];

        // Check if we already have a "Default" source from explicit config
        if (_sources.ContainsKey("Default"))
        {
            logger?.LogDebug("Skipping legacy Ollama config - 'Default' source already exists");
            return;
        }

        // Parse capability-specific models
        var capabilities = ParseCapabilities(ollamaSection.GetSection("Capabilities"), logger);

        // If no capability-specific config but DefaultModel is set, use for all capabilities
        if (capabilities.Count == 0 && !string.IsNullOrWhiteSpace(defaultModel))
        {
            capabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["Chat"] = new AiCapabilityConfig { Model = defaultModel },
                ["Embedding"] = new AiCapabilityConfig { Model = defaultModel }
            };
        }

        if (capabilities.Count == 0 && string.IsNullOrWhiteSpace(baseUrl))
        {
            // No useful configuration
            return;
        }

        RegisterSource(new AiSourceDefinition
        {
            Name = "Default",
            Provider = "ollama",
            ConnectionString = baseUrl,
            Capabilities = capabilities,
            Settings = new Dictionary<string, string>(),
            Origin = "legacy-config",
            IsAutoDiscovered = false
        });

        logger?.LogDebug(
            "Created 'Default' AI source from legacy Koan:Ai:Ollama config with {CapabilityCount} capabilities",
            capabilities.Count);
    }

    private static Dictionary<string, AiCapabilityConfig> ParseCapabilities(
        IConfigurationSection capabilitiesSection,
        ILogger? logger)
    {
        var capabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var capConfig in capabilitiesSection.GetChildren())
        {
            var capabilityName = capConfig.Key; // "Chat", "Embedding", "Vision"
            var model = capConfig["Model"];

            if (string.IsNullOrWhiteSpace(model))
            {
                logger?.LogWarning(
                    "Capability '{Capability}' has no model configured, skipping",
                    capabilityName);
                continue;
            }

            // Parse capability-specific options (temperature, max_tokens, etc.)
            var options = capConfig.GetChildren()
                .Where(c => c.Key != "Model" && c.Key != "AutoDownload")
                .ToDictionary<IConfigurationSection, string, object>(
                    c => c.Key,
                    c => ParseOptionValue(c.Value),
                    StringComparer.OrdinalIgnoreCase);

            var autoDownload = capConfig.GetValue<bool?>("AutoDownload") ?? true;

            capabilities[capabilityName] = new AiCapabilityConfig
            {
                Model = model,
                Options = options.Count > 0 ? options : null,
                AutoDownload = autoDownload
            };
        }

        return capabilities;
    }

    private static object ParseOptionValue(string? value)
    {
        if (value == null) return "";

        // Try to parse as number
        if (double.TryParse(value, out var number))
        {
            return number;
        }

        // Try to parse as boolean
        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        // Return as string
        return value;
    }

    /// <summary>
    /// Programmatically register a source (for runtime/testing scenarios or auto-discovery)
    /// </summary>
    /// <param name="source">Source definition to register</param>
    /// <exception cref="ArgumentException">Thrown when source name is empty</exception>
    public void RegisterSource(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Source name cannot be empty", nameof(source));

        _sources[source.Name] = source;
    }

    /// <summary>
    /// Get source definition by name (case-insensitive)
    /// </summary>
    /// <param name="name">Source name</param>
    /// <returns>Source definition or null if not found</returns>
    public AiSourceDefinition? GetSource(string name)
        => _sources.TryGetValue(name, out var source) ? source : null;

    /// <summary>
    /// Try to get source definition by name (case-insensitive)
    /// </summary>
    /// <param name="name">Source name</param>
    /// <param name="source">Source definition if found</param>
    /// <returns>True if source exists, false otherwise</returns>
    public bool TryGetSource(string name, out AiSourceDefinition? source)
        => _sources.TryGetValue(name, out source!);

    /// <summary>
    /// Get all registered source names
    /// </summary>
    public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();

    /// <summary>
    /// Get all registered sources
    /// </summary>
    public IReadOnlyCollection<AiSourceDefinition> GetAllSources() => _sources.Values.ToArray();

    /// <summary>
    /// Check if source exists (case-insensitive)
    /// </summary>
    public bool HasSource(string name) => _sources.ContainsKey(name);

    /// <summary>
    /// Get all sources in a specific group
    /// </summary>
    public IReadOnlyCollection<AiSourceDefinition> GetSourcesInGroup(string groupName)
        => _sources.Values
            .Where(s => string.Equals(s.Group, groupName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Priority)
            .ToArray();
}
