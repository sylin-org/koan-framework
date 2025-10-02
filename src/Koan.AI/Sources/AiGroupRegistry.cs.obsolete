using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources;

/// <summary>
/// Registry for AI source groups with policy and health monitoring configuration.
/// Groups enable fallback chains, load balancing, and automatic recovery.
/// </summary>
public sealed class AiGroupRegistry : IAiGroupRegistry
{
    private readonly ConcurrentDictionary<string, AiGroupDefinition> _groups =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Auto-discover groups from IConfiguration at "Koan:Ai:Groups:{name}".
    /// Also creates implicit groups for auto-discovered sources.
    /// </summary>
    /// <param name="config">Configuration to scan for groups</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        var groupsSection = config.GetSection("Koan:Ai:Groups");

        foreach (var groupConfig in groupsSection.GetChildren())
        {
            var groupName = groupConfig.Key;
            var policy = groupConfig["Policy"] ?? "Fallback";
            var stickySession = groupConfig.GetValue<bool?>("StickySession") ?? false;

            // Parse health check config
            var healthCheckSection = groupConfig.GetSection("HealthCheck");
            var healthCheck = new AiHealthCheckConfig
            {
                Enabled = healthCheckSection.GetValue<bool?>("Enabled") ?? true,
                IntervalSeconds = healthCheckSection.GetValue<int?>("IntervalSeconds") ?? 30,
                TimeoutSeconds = healthCheckSection.GetValue<int?>("TimeoutSeconds") ?? 5
            };

            // Parse circuit breaker config
            var circuitBreakerSection = groupConfig.GetSection("CircuitBreaker");
            var circuitBreaker = new AiCircuitBreakerConfig
            {
                FailureThreshold = circuitBreakerSection.GetValue<int?>("FailureThreshold") ?? 3,
                BreakDurationSeconds = circuitBreakerSection.GetValue<int?>("BreakDurationSeconds") ?? 30,
                RecoveryThreshold = circuitBreakerSection.GetValue<int?>("RecoveryThreshold") ?? 2
            };

            RegisterGroup(new AiGroupDefinition
            {
                Name = groupName,
                Policy = policy,
                HealthCheck = healthCheck,
                CircuitBreaker = circuitBreaker,
                StickySession = stickySession
            });

            logger?.LogDebug(
                "Discovered AI group '{GroupName}' with policy '{Policy}'",
                groupName,
                policy);
        }
    }

    /// <summary>
    /// Programmatically register a group (for runtime/testing scenarios or auto-discovery)
    /// </summary>
    /// <param name="group">Group definition to register</param>
    /// <exception cref="ArgumentException">Thrown when group name is empty</exception>
    public void RegisterGroup(AiGroupDefinition group)
    {
        if (string.IsNullOrWhiteSpace(group.Name))
            throw new ArgumentException("Group name cannot be empty", nameof(group));

        _groups[group.Name] = group;
    }

    /// <summary>
    /// Get group definition by name (case-insensitive)
    /// </summary>
    /// <param name="name">Group name</param>
    /// <returns>Group definition or null if not found</returns>
    public AiGroupDefinition? GetGroup(string name)
        => _groups.TryGetValue(name, out var group) ? group : null;

    /// <summary>
    /// Try to get group definition by name (case-insensitive)
    /// </summary>
    /// <param name="name">Group name</param>
    /// <param name="group">Group definition if found</param>
    /// <returns>True if group exists, false otherwise</returns>
    public bool TryGetGroup(string name, out AiGroupDefinition? group)
        => _groups.TryGetValue(name, out group!);

    /// <summary>
    /// Get all registered group names
    /// </summary>
    public IReadOnlyCollection<string> GetGroupNames() => _groups.Keys.ToArray();

    /// <summary>
    /// Get all registered groups
    /// </summary>
    public IReadOnlyCollection<AiGroupDefinition> GetAllGroups() => _groups.Values.ToArray();

    /// <summary>
    /// Check if group exists (case-insensitive)
    /// </summary>
    public bool HasGroup(string name) => _groups.ContainsKey(name);

    /// <summary>
    /// Ensure a group exists, creating a default one if not
    /// </summary>
    /// <param name="name">Group name</param>
    /// <returns>The group definition</returns>
    public AiGroupDefinition EnsureGroup(string name)
    {
        if (_groups.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var newGroup = new AiGroupDefinition
        {
            Name = name,
            Policy = "Fallback",
            HealthCheck = new AiHealthCheckConfig(),
            CircuitBreaker = new AiCircuitBreakerConfig()
        };

        RegisterGroup(newGroup);

        return newGroup;
    }
}
