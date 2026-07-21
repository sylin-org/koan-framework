using Koan.Cache.Abstractions.Capabilities;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Core.Capabilities;
using Koan.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Topology;

/// <summary>One immutable Local/Remote Cache topology compiled for the host.</summary>
internal sealed class CacheTopology
{
    public CacheTopology(
        IEnumerable<ICacheStore> stores,
        IOptions<CacheOptions> options,
        ILogger<CacheTopology> logger)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var catalog = ProviderCatalog<ICacheStore>.Compile(
            stores,
            static store => new ProviderCandidateDescriptor(
                store.Name,
                priority: ProviderMetadata.Priority(store.GetType())));
        Candidates = catalog.Candidates
            .Select(static candidate => new CacheStoreCandidate(
                candidate.Value,
                candidate.Id,
                candidate.Value.Placement,
                candidate.Priority,
                CacheCaps.Describe(candidate.Value, candidate.Id)))
            .ToArray();

        LocalRoute = Select(catalog, CacheStorePlacement.Local, options.Value.LocalProvider);
        RemoteRoute = Select(catalog, CacheStorePlacement.Remote, options.Value.RemoteProvider);

        if (IsLayered)
            logger.LogInformation("Koan.Cache topology: layered (L1={Local}, L2={Remote}).", Local!.Name, Remote!.Name);
        else if (IsLocalOnly)
            logger.LogInformation("Koan.Cache topology: local-only (L1={Local}).", Local!.Name);
        else if (IsRemoteOnly)
            logger.LogInformation("Koan.Cache topology: remote-only (L2={Remote}).", Remote!.Name);
        else
            logger.LogWarning("Koan.Cache topology has no eligible store.");
    }

    public IReadOnlyList<CacheStoreCandidate> Candidates { get; }
    public CacheStoreRoute? LocalRoute { get; }
    public CacheStoreRoute? RemoteRoute { get; }
    public ICacheStore? Local => LocalRoute?.Store;
    public ICacheStore? Remote => RemoteRoute?.Store;
    public ProviderSelectionReceipt? LocalReceipt => LocalRoute?.Receipt;
    public ProviderSelectionReceipt? RemoteReceipt => RemoteRoute?.Receipt;
    public bool HasAny => LocalRoute is not null || RemoteRoute is not null;
    public bool IsLayered => LocalRoute is not null && RemoteRoute is not null;
    public bool IsLocalOnly => LocalRoute is not null && RemoteRoute is null;
    public bool IsRemoteOnly => LocalRoute is null && RemoteRoute is not null;

    public void Require(CacheTier tier, string operation)
    {
        var available = tier switch
        {
            CacheTier.LocalOnly => LocalRoute is not null,
            CacheTier.RemoteOnly => RemoteRoute is not null,
            CacheTier.Layered => HasAny,
            _ => false
        };
        if (available) return;

        var correction = tier == CacheTier.LocalOnly
            ? "Reference a Local Cache adapter or choose Layered/RemoteOnly."
            : tier == CacheTier.RemoteOnly
                ? "Reference a Remote Cache adapter or choose Layered/LocalOnly."
                : "Reference a Cache store provider.";
        throw new InvalidOperationException(
            $"Koan Cache cannot perform {operation} with tier '{tier}' because that tier is unavailable. {correction}");
    }

    private CacheStoreRoute? Select(
        ProviderCatalog<ICacheStore> catalog,
        CacheStorePlacement placement,
        string? pinnedName)
    {
        var candidates = catalog.Candidates
            .Where(candidate => candidate.Value.Placement == placement)
            .ToArray();

        ProviderCandidate<ICacheStore>? selected;
        ProviderIntentPosture posture;
        string reason;
        if (!string.IsNullOrWhiteSpace(pinnedName))
        {
            var pinned = catalog.Find(pinnedName);
            if (pinned is null)
            {
                var choices = candidates.Select(static candidate => candidate.Id).ToArray();
                throw new InvalidOperationException(
                    $"Koan Cache cannot select {placement} provider '{pinnedName}' because it is not registered. " +
                    $"Candidates: {(choices.Length == 0 ? "none" : string.Join(", ", choices))}. " +
                    "Correct the provider name or reference the intended adapter; Koan will not weaken an explicit pin.");
            }

            var descriptor = catalog.Describe(pinned);
            if (pinned.Placement != placement)
            {
                throw new InvalidOperationException(
                    $"Koan Cache provider '{descriptor.Id}' is registered as {pinned.Placement} and cannot satisfy " +
                    $"the {placement} provider pin. Correct the pin or placement.");
            }

            selected = descriptor;
            posture = ProviderIntentPosture.Required;
            reason = "explicit-binding";
        }
        else
        {
            var store = catalog.Best(candidates, static (left, right) => right.Priority.CompareTo(left.Priority));
            if (store is null) return null;
            selected = catalog.Describe(store);
            posture = ProviderIntentPosture.Automatic;
            reason = "priority-selection";
        }

        var compiled = Candidates.Single(candidate => ReferenceEquals(candidate.Store, selected.Value));
        return new CacheStoreRoute(
            compiled.Store,
            compiled.Capabilities,
            new ProviderSelectionReceipt(
                placement == CacheStorePlacement.Local ? "cache:local" : "cache:remote",
                compiled.Id,
                posture,
                compiled.Priority,
                reason));
    }
}

internal sealed record CacheStoreCandidate(
    ICacheStore Store,
    string Id,
    CacheStorePlacement Placement,
    int Priority,
    CapabilitySet Capabilities);

internal sealed record CacheStoreRoute(
    ICacheStore Store,
    CapabilitySet Capabilities,
    ProviderSelectionReceipt Receipt);
