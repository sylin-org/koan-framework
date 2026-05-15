namespace Koan.Cache.Abstractions.Primitives;

/// <summary>
/// Options that govern a single cache read. Distinct from <c>CacheWriteOptions</c> so the
/// store contract can be strict about which side each call originates from.
/// </summary>
public sealed record CacheReadOptions(
    string? Region,
    string? ScopeId,
    CacheConsistencyMode Consistency,
    System.TimeSpan? AllowStaleFor)
{
    /// <summary>Default read options: no region/scope, stale-while-revalidate consistency, no stale window.</summary>
    public static CacheReadOptions Default { get; } = new(
        Region: null,
        ScopeId: null,
        Consistency: CacheConsistencyMode.StaleWhileRevalidate,
        AllowStaleFor: null);
}
