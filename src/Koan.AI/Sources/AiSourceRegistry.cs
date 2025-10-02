using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources;

/// <summary>
/// Registry for AI sources (collections of members).
/// Sources are discovered from configuration or registered programmatically.
///
/// ADR-0015: Sources are collections with priority and policy. Members are endpoints within sources.
/// No "Default" source is created - router elects highest-priority source with required capability.
/// </summary>
public sealed class AiSourceRegistry : IAiSourceRegistry
{
    private readonly ConcurrentDictionary<string, AiSourceDefinition> _sources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Auto-discover sources from IConfiguration at "Koan:Ai:Sources:{name}".
    /// NO "Default" source creation - router handles election.
    /// </summary>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        // 1. Discover explicit sources from Koan:Ai:Sources
        DiscoverExplicitSources(config, logger);

        // 2. Handle backward-compatible Koan:Ai:Ollama config (creates "Default" source if configured)
        DiscoverLegacyOllamaConfig(config, logger);

        // NO implicit "Default" source creation - ADR-0015 requires election
    }

    private void DiscoverExplicitSources(IConfiguration config, ILogger? logger)
    {
        var sourcesSection = config.GetSection("Koan:Ai:Sources");

        foreach (var sourceConfig in sourcesSection.GetChildren())
        {
            var sourceName = sourceConfig.Key;

            // Validate source name doesn't contain :: (reserved for members)
            if (sourceName.Contains("::"))
            {
                logger?.LogWarning(
                    "Source name '{SourceName}' contains '::' which is reserved for members - skipping",
                    sourceName);
                continue;
            }

            var provider = sourceConfig["Provider"];
            var priority = sourceConfig.GetValue<int?>("Priority") ?? 100; // Explicit config defaults to high priority
            var policy = sourceConfig["Policy"] ?? "Fallback";

            if (string.IsNullOrWhiteSpace(provider))
            {
                logger?.LogWarning(
                    "AI Source '{SourceName}' has no provider configured, skipping",
                    sourceName);
                continue;
            }

            // Parse capabilities
            var capabilities = ParseCapabilities(sourceConfig.GetSection("Capabilities"), logger);

            // Parse members from provider-specific configuration
            // For now, we'll create empty members list - discovery service will populate
            var members = new List<AiMemberDefinition>();

            // Check if provider section has URLs
            var providerSection = sourceConfig.GetSection(provider);
            if (providerSection.Exists())
            {
                var urls = providerSection.GetSection("Urls").Get<string[]>();
                if (urls != null && urls.Length > 0)
                {
                    for (int i = 0; i < urls.Length; i++)
                    {
                        members.Add(new AiMemberDefinition
                        {
                            Name = $"{sourceName}::explicit-{i + 1}",
                            ConnectionString = urls[i],
                            Order = i,
                            Origin = "config-urls",
                            IsAutoDiscovered = false
                        });
                    }
                }
            }

            if (members.Count == 0)
            {
                logger?.LogWarning(
                    "Source '{SourceName}' has no members configured - discovery service should populate",
                    sourceName);
                // Don't skip - discovery service may add members later
            }

            RegisterSource(new AiSourceDefinition
            {
                Name = sourceName,
                Provider = provider,
                Priority = priority,
                Policy = policy,
                Members = members,
                Capabilities = (IReadOnlyDictionary<string, AiCapabilityConfig>)capabilities,
                Origin = "explicit-config",
                IsAutoDiscovered = false
            });

            logger?.LogDebug(
                "Discovered AI source '{SourceName}' with provider '{Provider}', priority {Priority}, {MemberCount} members",
                sourceName,
                provider,
                priority,
                members.Count);
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

        // Check if we already have an "ollama" source from explicit config
        if (_sources.ContainsKey("ollama"))
        {
            logger?.LogDebug("Skipping legacy Ollama config - 'ollama' source already exists from explicit config");
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

        // Create legacy "Default" source only if user explicitly configured Ollama
        // This maintains backward compatibility
        var members = new List<AiMemberDefinition>();

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            members.Add(new AiMemberDefinition
            {
                Name = "Default::legacy",
                ConnectionString = baseUrl,
                Order = 0,
                Origin = "legacy-config",
                IsAutoDiscovered = false
            });
        }

        RegisterSource(new AiSourceDefinition
        {
            Name = "Default", // Legacy name for backward compat
            Provider = "ollama",
            Priority = 50,
            Policy = "Fallback",
            Members = members,
            Capabilities = (IReadOnlyDictionary<string, AiCapabilityConfig>)capabilities,
            Origin = "legacy-config",
            IsAutoDiscovered = false
        });

        logger?.LogDebug(
            "Created 'Default' AI source from legacy Koan:Ai:Ollama config with {CapabilityCount} capabilities, {MemberCount} members",
            capabilities.Count,
            members.Count);
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
    /// Programmatically register a source. Validates source name doesn't contain ::.
    /// Throws if source name collides with existing source from different origin.
    /// </summary>
    public void RegisterSource(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Source name cannot be empty", nameof(source));

        // Validate source name doesn't contain ::
        if (source.Name.Contains("::"))
            throw new ArgumentException(
                $"Source name '{source.Name}' cannot contain '::' - this separator is reserved for members",
                nameof(source));

        // Check for collision
        if (_sources.TryGetValue(source.Name, out var existing))
        {
            // Allow same-origin re-registration (e.g., discovery service updating members)
            if (existing.Origin != source.Origin)
            {
                throw new InvalidOperationException(
                    $"Source name collision detected: '{source.Name}' already registered by {existing.Origin}. " +
                    $"Use a different name or remove conflicting configuration.");
            }
        }

        _sources[source.Name] = source;
    }

    public AiSourceDefinition? GetSource(string name)
        => _sources.TryGetValue(name, out var source) ? source : null;

    public bool TryGetSource(string name, out AiSourceDefinition? source)
        => _sources.TryGetValue(name, out source!);

    public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();

    public IReadOnlyCollection<AiSourceDefinition> GetAllSources() => _sources.Values.ToArray();

    public bool HasSource(string name) => _sources.ContainsKey(name);

    public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
        => _sources.Values
            .Where(s => s.Capabilities.ContainsKey(capabilityName))
            .OrderByDescending(s => s.Priority)
            .ToArray();
}
