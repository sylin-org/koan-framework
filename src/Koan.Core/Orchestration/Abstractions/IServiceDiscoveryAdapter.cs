using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Contract for autonomous service discovery adapters.
/// Koan service adapters derive from <see cref="ServiceDiscoveryAdapterBase"/> so every adapter participates
/// in the same precedence, activated-contributor, normalization, health, fallback, and reporting lifecycle.
/// Implement this interface directly only for synthetic/test adapters or when a specific architectural
/// decision defines a different concern boundary.
/// </summary>
public interface IServiceDiscoveryAdapter
{
    /// <summary>Primary service identifier (e.g., "mongo", "ollama")</summary>
    string ServiceName { get; }

    /// <summary>Alternative identifiers this adapter handles (e.g., ["mongodb"] for mongo)</summary>
    string[] Aliases { get; }

    /// <summary>Adapter priority for service name conflicts (higher wins)</summary>
    int Priority { get; }

    /// <summary>
    /// Autonomous discovery - adapter reads its own KoanServiceAttribute,
    /// tries discovery strategies, validates health, and decides what to use.
    /// </summary>
    Task<AdapterDiscoveryResult> Discover(
        DiscoveryContext context,
        CancellationToken cancellationToken = default);
}
