using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Topology;

/// <summary>
/// Resolves a (Local, Remote) tier pair from registered cache stores using:
///   1. Explicit config pin (<c>CacheOptions.LocalProvider</c> / <c>RemoteProvider</c> by <c>ICacheStore.Name</c>);
///   2. Highest <c>[ProviderPriority]</c> among stores with matching <c>Placement</c>;
///   3. First registered store with matching <c>Placement</c>;
///   4. Null (single-tier deployment).
/// </summary>
internal sealed class CacheTopologyResolver
{
    private readonly ILogger<CacheTopologyResolver> _logger;

    public CacheTopologyResolver(ILogger<CacheTopologyResolver> logger)
    {
        _logger = logger;
    }

    public CacheTopology Resolve(IEnumerable<ICacheStore> stores, CacheOptions options)
    {
        var snapshot = stores?.ToList() ?? new List<ICacheStore>();
        if (snapshot.Count == 0)
        {
            _logger.LogWarning("Koan.Cache: no stores registered — layered cache will no-op.");
            return CacheTopology.Empty;
        }

        var local = ResolveTier(snapshot, CacheStorePlacement.Local, options.LocalProvider);
        var remote = ResolveTier(snapshot, CacheStorePlacement.Remote, options.RemoteProvider);

        if (local is not null && remote is not null)
            _logger.LogInformation("Koan.Cache topology: layered (L1={Local}, L2={Remote}).", local.Name, remote.Name);
        else if (local is not null)
            _logger.LogInformation("Koan.Cache topology: local-only (L1={Local}, no Remote registered).", local.Name);
        else if (remote is not null)
            _logger.LogInformation("Koan.Cache topology: remote-only (L2={Remote}, no Local registered).", remote.Name);
        else
            _logger.LogWarning("Koan.Cache topology: no matching tiers — layered cache will no-op.");

        return new CacheTopology(local, remote);
    }

    private ICacheStore? ResolveTier(IReadOnlyList<ICacheStore> stores, CacheStorePlacement placement, string? pinnedName)
    {
        if (!string.IsNullOrWhiteSpace(pinnedName))
        {
            var pinned = stores.FirstOrDefault(s =>
                s.Placement == placement &&
                s.Name.Equals(pinnedName, StringComparison.OrdinalIgnoreCase));

            if (pinned is not null) return pinned;

            _logger.LogWarning(
                "Koan.Cache: pinned {Placement} provider '{Pinned}' not registered. Falling back to priority/order resolution.",
                placement, pinnedName);
        }

        return stores
            .Where(s => s.Placement == placement)
            .OrderByDescending(GetPriority)
            .ThenBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetPriority(ICacheStore store)
        => store.GetType().GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0;
}
