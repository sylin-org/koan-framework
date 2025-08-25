using System.Collections.Concurrent;
using Sora.Web.Auth.Domain;

namespace Sora.Web.Auth.Infrastructure;

internal sealed class InMemoryExternalIdentityStore : IExternalIdentityStore
{
    private readonly ConcurrentDictionary<(string userId, string provider, string keyHash), ExternalIdentity> _store = new();

    public Task<IReadOnlyList<ExternalIdentity>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        var list = _store.Values.Where(v => v.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<ExternalIdentity>>(list);
    }

    public Task LinkAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        _store[(identity.UserId, identity.Provider, identity.ProviderKeyHash)] = identity;
        return Task.CompletedTask;
    }

    public Task UnlinkAsync(string userId, string provider, string providerKeyHash, CancellationToken ct = default)
    {
        _store.TryRemove((userId, provider, providerKeyHash), out _);
        return Task.CompletedTask;
    }
}