using Koan.Web.Auth.Domain;

namespace Koan.Identity.Infrastructure;

/// <summary>
/// SEC-0007 — the durable <see cref="IUserStore"/> backed by the <see cref="Identity"/> entity, replacing the
/// vestigial in-memory stub (whose <c>Exists</c> always returned false). Existence is now a real lookup against
/// the person store.
/// </summary>
internal sealed class IdentityUserStore : IUserStore
{
    public async Task<bool> Exists(string userId, CancellationToken ct = default)
        => !string.IsNullOrWhiteSpace(userId)
           && await Identity.Get(userId, ct).ConfigureAwait(false) is not null;
}
