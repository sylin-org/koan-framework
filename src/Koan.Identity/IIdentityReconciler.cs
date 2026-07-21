namespace Koan.Identity;

/// <summary>
/// Upserts the durable <see cref="Identity"/> (and its verified-email factor) from sign-in claims. The single
/// reconciliation core every wired trigger delegates to (the auth flow handler, the external-identity store, the
/// dev seed). <b>Idempotent</b>: re-running for the same subject updates and never duplicates. By contract
/// <c>Identity.Id == claims.Subject</c>, so the bare-string <c>…Subject</c> / <c>Membership.IdentityId</c>
/// references resolve to the returned person.
/// </summary>
public interface IIdentityReconciler
{
    /// <summary>Upsert the person + email factor for <paramref name="claims"/> and return the durable <see cref="Identity"/>.</summary>
    Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default);
}
