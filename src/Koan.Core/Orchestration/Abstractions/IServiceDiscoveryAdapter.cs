using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Contract for autonomous service discovery adapters.
/// Each service adapter implements this to handle its own discovery process.
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
    Task<AdapterDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default);
}