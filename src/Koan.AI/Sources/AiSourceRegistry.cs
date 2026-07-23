using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Sources;
using Koan.AI.Infrastructure;

namespace Koan.AI.Sources;

/// <summary>
/// Registry for AI sources (collections of members).
/// Sources are discovered from configuration or registered programmatically.
///
/// ADR-0015: Sources are collections with priority and policy. Members are endpoints within sources.
/// No "Default" source is created - router elects highest-priority source with required capability.
/// </summary>
public sealed class AiSourceRegistry : IAiSourceRegistry, IAiSourceRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, AiSourceRuntimeSnapshot> _sources =
        new(StringComparer.OrdinalIgnoreCase);
    private long _revision;

    /// <summary>
    /// Auto-discover sources from IConfiguration at "<see cref="ConfigurationConstants.Sources.Section"/>:{name}".
    /// NO "Default" source creation - router handles election.
    /// </summary>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        // 1. Discover explicit sources from Koan:Ai:Sources
        DiscoverExplicitSources(config, logger);

        // Provider packages own provider configuration and publish their default sources during
        // the compiled activation pass. The concern registry only owns explicitly named sources.
    }

    private void DiscoverExplicitSources(IConfiguration config, ILogger? logger)
    {
        var sourcesSection = config.GetSection(ConfigurationConstants.Sources.Section);

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
        => Apply(source);

    AiSourceDefinition IAiSourceRuntimeRegistry.Apply(AiSourceDefinition source)
        => Apply(source);

    private AiSourceDefinition Apply(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Source name cannot be empty", nameof(source));
        if (string.IsNullOrWhiteSpace(source.Provider))
            throw new ArgumentException("Source provider cannot be empty", nameof(source));
        if (source.Members is null)
            throw new ArgumentException("Source members cannot be null", nameof(source));

        // Validate source name doesn't contain ::
        if (source.Name.Contains("::"))
            throw new ArgumentException(
                $"Source name '{source.Name}' cannot contain '::' - this separator is reserved for members",
                nameof(source));

        var sourceName = source.Name.Trim();

        // Check for collision
        if (_sources.TryGetValue(sourceName, out var existing))
        {
            // Allow same-origin re-registration (e.g., discovery service updating members)
            if (!string.Equals(existing.Source.Origin, source.Origin, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source name collision detected: '{source.Name}' already registered by {existing.Source.Origin}. " +
                    $"Use a different name or remove conflicting configuration.");
            }
        }

        var normalized = source with
        {
            Name = source.Name.Trim(),
            Provider = source.Provider.Trim(),
            Members = source.Members
                .Select(member => member with { HealthState = MemberHealthState.Unknown })
                .ToList()
        };
        _sources[sourceName] = new AiSourceRuntimeSnapshot(
            normalized,
            Interlocked.Increment(ref _revision));
        return normalized;
    }

    public AiSourceDefinition? GetSource(string name)
        => _sources.TryGetValue(name, out var source) ? source.Source : null;

    public bool TryGetSource(string name, out AiSourceDefinition? source)
    {
        if (_sources.TryGetValue(name, out var snapshot))
        {
            source = snapshot.Source;
            return true;
        }

        source = null;
        return false;
    }

    public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();

    public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
        => _sources.Values.Select(snapshot => snapshot.Source).ToArray();

    public bool HasSource(string name) => _sources.ContainsKey(name);

    public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
        => _sources.Values
            .Select(snapshot => snapshot.Source)
            .Where(source => source.IsEnabled && source.Capabilities.ContainsKey(capabilityName))
            .OrderByDescending(source => source.Priority)
            .ToArray();

    bool IAiSourceRuntimeRegistry.SetEnabled(string name, bool enabled)
    {
        while (_sources.TryGetValue(name, out var existing))
        {
            if (existing.Source.IsEnabled == enabled) return true;
            var updated = new AiSourceRuntimeSnapshot(
                existing.Source with { IsEnabled = enabled },
                Interlocked.Increment(ref _revision));
            if (_sources.TryUpdate(name, updated, existing)) return true;
        }

        return false;
    }

    bool IAiSourceRuntimeRegistry.Remove(string name, string? expectedOrigin)
    {
        while (_sources.TryGetValue(name, out var existing))
        {
            if (!string.IsNullOrWhiteSpace(expectedOrigin) &&
                !string.Equals(existing.Source.Origin, expectedOrigin, StringComparison.Ordinal))
            {
                return false;
            }

            if (_sources.TryRemove(new KeyValuePair<string, AiSourceRuntimeSnapshot>(name, existing)))
            {
                Interlocked.Increment(ref _revision);
                return true;
            }
        }

        return false;
    }

    IReadOnlyCollection<AiSourceRuntimeSnapshot> IAiSourceRuntimeRegistry.GetRuntimeSources(bool includeDisabled)
        => _sources.Values
            .Where(snapshot => includeDisabled || snapshot.Source.IsEnabled)
            .ToArray();

    bool IAiSourceRuntimeRegistry.TrySetMemberHealth(
        string sourceName,
        long revision,
        string memberName,
        MemberHealthState state)
    {
        if (!_sources.TryGetValue(sourceName, out var snapshot) || snapshot.Revision != revision)
        {
            return false;
        }

        var member = snapshot.Source.Members.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));
        if (member is null) return false;

        member.HealthState = state;
        return _sources.TryGetValue(sourceName, out var current) && current.Revision == revision;
    }
}
