using Koan.Web.Auth.Domain;
using System.Collections.Concurrent;

namespace Koan.Web.Auth.Infrastructure;

internal sealed class InMemoryExternalIdentityStore : IExternalIdentityStore
{
    private readonly ConcurrentDictionary<(string userId, string provider, string keyHash), ExternalIdentity> _store = new();

    public Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default)
    {
        var list = _store.Values.Where(v => v.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<ExternalIdentity>>(list);
    }

    public Task Link(ExternalIdentity identity, CancellationToken ct = default)
    {
        _store[(identity.UserId, identity.Provider, identity.ProviderKeyHash)] = identity;
        return Task.CompletedTask;
    }

    public Task Unlink(string userId, string provider, string providerKeyHash, CancellationToken ct = default)
    {
        _store.TryRemove((userId, provider, providerKeyHash), out _);
        return Task.CompletedTask;
    }
}