using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Identity;

/// <summary>Internal subject-aware write seam; application cache contracts remain type-neutral.</summary>
internal interface ICacheIdentityWriter
{
    ValueTask<bool> Remove(CacheKey key, Type? subject, CancellationToken ct);
}
