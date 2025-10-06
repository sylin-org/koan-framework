using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheScopeAccessor
{
    CacheScopeContext Current { get; }

    CacheScopeContext Push(string scopeId, string? region);

    void Pop(CacheScopeContext context);
}
