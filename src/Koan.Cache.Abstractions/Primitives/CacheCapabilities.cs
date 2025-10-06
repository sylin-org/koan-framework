using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheCapabilities(
    bool SupportsBinary,
    bool SupportsPubSubInvalidation,
    bool SupportsCompareExchange,
    bool SupportsRegionScoping,
    IReadOnlySet<string> Hints)
{
    public static CacheCapabilities None { get; } = new(false, false, false, false, new HashSet<string>());

    public bool SupportsTags => Hints.Contains("tags");

    public bool SupportsStaleWhileRevalidate => Hints.Contains("stale-while-revalidate");

    public bool SupportsScopedKeys => SupportsRegionScoping || Hints.Contains("scoped-keys");

    public bool SupportsSingleflightAssist => Hints.Contains("singleflight");
}
