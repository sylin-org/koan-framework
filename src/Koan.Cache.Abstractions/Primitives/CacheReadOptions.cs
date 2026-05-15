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
    /// <summary>
    /// Default read options: no region/scope, <see cref="CacheConsistencyMode.Strict"/> consistency,
    /// no stale window. Per ARCH-0078, default reads are "fresh or null" — SWR is an explicit
    /// per-call opt-in via <see cref="AllowStaleFor"/>.
    /// </summary>
    public static CacheReadOptions Default { get; } = new(
        Region: null,
        ScopeId: null,
        Consistency: CacheConsistencyMode.Strict,
        AllowStaleFor: null);
}
