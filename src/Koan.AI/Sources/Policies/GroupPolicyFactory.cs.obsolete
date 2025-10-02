using System;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources.Policies;

/// <summary>
/// Factory for creating group policy instances based on policy name
/// </summary>
public static class GroupPolicyFactory
{
    /// <summary>
    /// Create policy instance from policy name
    /// </summary>
    /// <param name="policyName">Policy name (e.g., "Fallback", "RoundRobin")</param>
    /// <param name="adapterRegistry">Adapter registry for resolving sources to adapters</param>
    /// <returns>Policy instance</returns>
    /// <exception cref="ArgumentException">Unknown policy name</exception>
    public static IGroupPolicy CreatePolicy(
        string policyName,
        Contracts.Routing.IAiAdapterRegistry adapterRegistry)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            policyName = "Fallback";
        }

        return policyName.ToLowerInvariant() switch
        {
            "fallback" => new FallbackPolicy(adapterRegistry),
            "roundrobin" or "round-robin" or "rr" => new RoundRobinPolicy(adapterRegistry),
            "weightedroundrobin" or "weighted-round-robin" or "wrr" => new RoundRobinPolicy(adapterRegistry), // For now, same as RR
            _ => throw new ArgumentException($"Unknown policy: {policyName}", nameof(policyName))
        };
    }
}
