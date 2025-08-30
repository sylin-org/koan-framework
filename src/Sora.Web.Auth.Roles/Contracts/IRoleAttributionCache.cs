using System.Collections.Concurrent;

namespace Sora.Web.Auth.Roles.Contracts;

public interface IRoleAttributionCache
{
    RoleAttributionResult? TryGet(string principalId);
    void Set(string principalId, RoleAttributionResult result);
    void Clear();
}

internal sealed class InMemoryRoleAttributionCache : IRoleAttributionCache
{
    private readonly ConcurrentDictionary<string, RoleAttributionResult> _cache = new(StringComparer.Ordinal);
    public RoleAttributionResult? TryGet(string principalId) => _cache.TryGetValue(principalId, out var v) ? v : null;
    public void Set(string principalId, RoleAttributionResult result) => _cache[principalId] = result;
    public void Clear() => _cache.Clear();
}
