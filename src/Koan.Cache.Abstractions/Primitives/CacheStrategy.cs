namespace Koan.Cache.Abstractions.Primitives;

public enum CacheStrategy
{
    GetOrSet,
    GetOnly,
    SetOnly,
    Invalidate,
    NoCache
}
