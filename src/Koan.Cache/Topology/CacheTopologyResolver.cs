using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Topology;

/// <summary>
/// Resolves a (Local, Remote) tier pair from registered cache stores using:
///   1. Explicit config pin (<c>CacheOptions.LocalProvider</c> / <c>RemoteProvider</c> by <c>ICacheStore.Name</c>);
///   2. Highest <c>[ProviderPriority]</c> among stores with matching <c>Placement</c>;
///   3. Stable provider name tie-break;
///   4. Null (single-tier deployment). Invalid explicit pins fail boot rather than weakening intent.
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
        var providers = ProviderCatalog<ICacheStore>.Compile(
            stores ?? [],
            static store => new ProviderCandidateDescriptor(
                store.Name,
                priority: ProviderMetadata.Priority(store.GetType())));
        if (providers.Candidates.Count == 0)
        {
            _logger.LogWarning("Koan.Cache: no stores registered — layered cache will no-op.");
            return CacheTopology.Empty;
        }

        var local = ResolveTier(providers, CacheStorePlacement.Local, options.LocalProvider);
        var remote = ResolveTier(providers, CacheStorePlacement.Remote, options.RemoteProvider);

        if (local.Store is not null && remote.Store is not null)
            _logger.LogInformation("Koan.Cache topology: layered (L1={Local}, L2={Remote}).", local.Store.Name, remote.Store.Name);
        else if (local.Store is not null)
            _logger.LogInformation("Koan.Cache topology: local-only (L1={Local}, no Remote registered).", local.Store.Name);
        else if (remote.Store is not null)
            _logger.LogInformation("Koan.Cache topology: remote-only (L2={Remote}, no Local registered).", remote.Store.Name);
        else
            _logger.LogWarning("Koan.Cache topology: no matching tiers — layered cache will no-op.");

        return new CacheTopology(local.Store, remote.Store, local.Receipt, remote.Receipt);
    }

    private static TierSelection ResolveTier(
        ProviderCatalog<ICacheStore> providers,
        CacheStorePlacement placement,
        string? pinnedName)
    {
        var candidates = providers.Candidates
            .Where(candidate => candidate.Value.Placement == placement)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(pinnedName))
        {
            var pinned = providers.Find(pinnedName);

            if (pinned?.Placement == placement)
            {
                var candidate = providers.Describe(pinned);
                return Selected(candidate, placement, ProviderIntentPosture.Required, "explicit-binding");
            }

            var choices = candidates.Select(static candidate => candidate.Id).ToArray();
            throw new InvalidOperationException(
                $"Koan Cache cannot select {placement} provider '{pinnedName}' because it is not registered. " +
                $"Candidates: {(choices.Length == 0 ? "none" : string.Join(", ", choices))}. " +
                "Correct the provider name or reference the intended adapter; Koan will not weaken an explicit pin.");
        }

        var selected = providers.Best(
            candidates,
            static (left, right) => right.Priority.CompareTo(left.Priority));
        return selected is null
            ? new TierSelection(null, null)
            : Selected(
                providers.Describe(selected),
                placement,
                ProviderIntentPosture.Automatic,
                "priority-selection");
    }

    private static TierSelection Selected(
        ProviderCandidate<ICacheStore> candidate,
        CacheStorePlacement placement,
        ProviderIntentPosture intent,
        string reason)
        => new(
            candidate.Value,
            new ProviderSelectionReceipt(
                placement == CacheStorePlacement.Local ? "cache:local" : "cache:remote",
                candidate.Id,
                intent,
                candidate.Priority,
                reason));

    private sealed record TierSelection(ICacheStore? Store, ProviderSelectionReceipt? Receipt);
}
