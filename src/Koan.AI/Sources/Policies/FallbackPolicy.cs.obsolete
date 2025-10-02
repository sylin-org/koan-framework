using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources.Policies;

/// <summary>
/// Fallback policy: try sources in priority order, skipping unhealthy ones.
/// Highest priority source is tried first, falling back to lower priority on failure.
/// </summary>
public sealed class FallbackPolicy : IGroupPolicy
{
    private readonly IAiAdapterRegistry _adapterRegistry;

    public FallbackPolicy(IAiAdapterRegistry adapterRegistry)
    {
        _adapterRegistry = adapterRegistry;
    }

    /// <inheritdoc />
    public string Name => "Fallback";

    /// <inheritdoc />
    public IAiAdapter? SelectAdapter(
        IReadOnlyList<AiSourceDefinition> sources,
        ISourceHealthRegistry healthRegistry)
    {
        // Sources already ordered by priority (descending)
        foreach (var source in sources)
        {
            // Skip unhealthy sources (circuit open)
            if (!healthRegistry.IsAvailable(source.Name))
            {
                continue;
            }

            // Find adapter matching this source
            var adapter = FindAdapterForSource(source);
            if (adapter != null)
            {
                return adapter;
            }
        }

        // No healthy source found - try unhealthy ones as last resort
        foreach (var source in sources)
        {
            var adapter = FindAdapterForSource(source);
            if (adapter != null)
            {
                return adapter;
            }
        }

        return null;
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
