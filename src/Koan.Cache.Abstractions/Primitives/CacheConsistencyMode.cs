namespace Koan.Cache.Abstractions.Primitives;

public enum CacheConsistencyMode
{
    Strict,
    StaleWhileRevalidate,
    PassthroughOnFailure
}
