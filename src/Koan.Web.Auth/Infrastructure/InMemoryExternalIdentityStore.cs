using Koan.Web.Auth.Domain;
using System.Collections.Concurrent;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// The store Web Auth uses when nothing durable replaces it. It knows no people: a login's subject stands for its user, so
/// nothing binds here.
/// </summary>
internal sealed class InMemoryExternalIdentityStore : IExternalIdentityStore
{
    private readonly ConcurrentDictionary<(string subject, string provider), ExternalIdentity> _store = new();

    public Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default)
    {
        var list = _store.Where(entry => entry.Key.subject == userId).Select(static entry => entry.Value).ToList();
        return Task.FromResult<IReadOnlyList<ExternalIdentity>>(list);
    }

    public Task Link(ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity.Subject);
        _store[(identity.Subject, identity.Provider)] = identity;
        return Task.CompletedTask;
    }

    public Task<BindOutcome> Bind(System.Security.Claims.ClaimsPrincipal signedIn, ExternalIdentity identity, CancellationToken ct = default)
        => throw new NotSupportedException("Binding a login to a person needs a store that knows people; reference Koan.Identity.");
}
