namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheScopeContext(string? ScopeId, string? Region)
{
    public static CacheScopeContext Empty { get; } = new(null, null);

    public bool HasScope => !string.IsNullOrWhiteSpace(ScopeId);
}
