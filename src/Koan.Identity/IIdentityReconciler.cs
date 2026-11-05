namespace Koan.Identity;

/// <summary>
/// Upserts the durable <see cref="Identity"/> (and its verified-email factor) a sign-in arrives as. The single
/// reconciliation core every wired trigger delegates to (the auth flow handler, the external-identity store, the dev
/// seed). A provider's sign-in arrives as the person its login is bound to (<see cref="ExternalIdentityLink"/>); a login
/// seen for the first time is bound to a new person, whose id is Koan's own, a GUID v7, and never the provider's
/// subject. A sign-in no provider made is the application's statement, and its subject is the person's id.
/// <b>Idempotent</b>: re-running for the same login updates and never duplicates.
/// </summary>
public interface IIdentityReconciler
{
    /// <summary>Upsert the person + email factor for <paramref name="claims"/> and return the durable <see cref="Identity"/>.</summary>
    Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default);
}
