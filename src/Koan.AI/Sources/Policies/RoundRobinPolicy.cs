using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources.Policies;

/// <summary>
/// Round-robin policy: distribute requests evenly across healthy sources.
/// Skips unhealthy sources automatically.
/// </summary>
public sealed class RoundRobinPolicy : IGroupPolicy
{
    private readonly IAiAdapterRegistry _adapterRegistry;
    private int _counter;

    public RoundRobinPolicy(IAiAdapterRegistry adapterRegistry)
    {
        _adapterRegistry = adapterRegistry;
    }

    /// <inheritdoc />
    public string Name => "RoundRobin";

    /// <inheritdoc />
    public IAiAdapter? SelectAdapter(
        IReadOnlyList<AiSourceDefinition> sources,
        ISourceHealthRegistry healthRegistry)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        // Filter to only healthy sources
        var healthySources = sources
            .Where(s => healthRegistry.IsAvailable(s.Name))
            .ToList();

        if (healthySources.Count == 0)
        {
            // No healthy sources - try all as fallback
            healthySources = sources.ToList();
        }

        if (healthySources.Count == 0)
        {
            return null;
        }

        // Round-robin selection
        var index = (int)((uint)Interlocked.Increment(ref _counter) % healthySources.Count);
        var source = healthySources[index];

        return FindAdapterForSource(source);
    }

    private IAiAdapter? FindAdapterForSource(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.ConnectionString))
        {
            // No connection string - use first adapter with matching provider
            return _adapterRegistry.All
                .FirstOrDefault(a => a.Id.Contains(source.Provider, System.StringComparison.OrdinalIgnoreCase));
        }

        // Parse source URL to extract host:port
        var sourceUri = TryParseUri(source.ConnectionString);
        if (sourceUri == null)
        {
            return null;
        }

        var sourceHost = sourceUri.Host;
        var sourcePort = sourceUri.Port;

        // Find adapter whose ID contains matching host:port
        foreach (var adapter in _adapterRegistry.All)
        {
            // Adapter IDs are like "ollama@host.docker.internal:11434"
            if (adapter.Id.Contains($"{sourceHost}:{sourcePort}", System.StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }

            // Also try matching just the host for standard ports
            if (adapter.Id.Contains(sourceHost, System.StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }
        }

        return null;
    }

    private static System.Uri? TryParseUri(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return null;
        }

        try
        {
            return new System.Uri(uriString);
        }
        catch
        {
            return null;
        }
    }
}
