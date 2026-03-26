using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheCapabilities(
    bool SupportsBinary,
    bool SupportsPubSubInvalidation,
    bool SupportsCompareExchange,
    bool SupportsRegionScoping,
    IReadOnlySet<string> Hints,
    bool SupportsSharedAccess = false,
    bool SupportsPersistence = false)
{
    public static CacheCapabilities None { get; } = new(false, false, false, false, new HashSet<string>());

    public bool SupportsTags => Hints.Contains("tags");

    public bool SupportsStaleWhileRevalidate => Hints.Contains("stale-while-revalidate");

    public bool SupportsScopedKeys => SupportsRegionScoping || Hints.Contains("scoped-keys");

    public bool SupportsSingleflightAssist => Hints.Contains("singleflight");

    /// <summary>True when this provider is suitable as an L2 (remote/shared) tier.</summary>
    public bool IsRemoteCapable => SupportsSharedAccess || SupportsPubSubInvalidation;

    /// <summary>True when this provider is local-only (L1 tier candidate).</summary>
    public bool IsLocalOnly => !IsRemoteCapable;
}
