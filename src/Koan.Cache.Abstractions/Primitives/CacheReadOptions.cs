namespace Koan.Cache.Abstractions.Primitives;

/// <summary>
/// Options that govern a single cache read. Distinct from <c>CacheWriteOptions</c> so the
/// store contract can be strict about which side each call originates from.
/// </summary>
public sealed record CacheReadOptions(
    string? Region,
    string? ScopeId,
    System.TimeSpan? AllowStaleFor)
{
    /// <summary>
    /// Default read options are fresh-or-miss with no stale-serving window.
    /// </summary>
    public static CacheReadOptions Default { get; } = new(
        Region: null,
        ScopeId: null,
        AllowStaleFor: null);
}
