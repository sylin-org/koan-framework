using Koan.Cache.Abstractions.Stores;
using Koan.Core.Providers;

namespace Koan.Cache.Topology;

/// <summary>
/// Resolved tier assignment for the layered cache. Either side may be null in single-tier
/// deployments — the layered cache degrades gracefully.
/// </summary>
internal sealed record CacheTopology(
    ICacheStore? Local,
    ICacheStore? Remote,
    ProviderSelectionReceipt? LocalReceipt = null,
    ProviderSelectionReceipt? RemoteReceipt = null)
{
    /// <summary>Empty topology — no tiers wired. Layered cache no-ops.</summary>
    public static CacheTopology Empty { get; } = new(null, null);

    public bool HasAny => Local is not null || Remote is not null;
    public bool IsLayered => Local is not null && Remote is not null;
    public bool IsLocalOnly => Local is not null && Remote is null;
    public bool IsRemoteOnly => Local is null && Remote is not null;
}
