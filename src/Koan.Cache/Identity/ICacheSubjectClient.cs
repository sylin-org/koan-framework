using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Identity;

/// <summary>
/// Internal subject-aware cache seam. Domain-facing facets name the subject; Cache owns
/// physical identity, segmentation, topology, and provider behavior behind this contract.
/// </summary>
internal interface ICacheSubjectClient
{
    ValueTask<bool> Remove(CacheKey key, Type? subject, CancellationToken ct);
    ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, Type? subject, CancellationToken ct);
    ValueTask<long> CountTags(IReadOnlyCollection<string> tags, Type? subject, CancellationToken ct);
}
