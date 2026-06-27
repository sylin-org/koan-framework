using Koan.Data.Core;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// The idempotent reconciliation core (SEC-0007 P0). Upserts the durable <see cref="Identity"/> keyed on the
/// claims subject and attaches the verified-email factor. Never clobbers user-owned display fields with an IdP
/// backfill. Pure data operations over <c>Entity&lt;T&gt;</c> — no ambient reads.
/// </summary>
internal sealed class IdentityReconciler : IIdentityReconciler
{
    public async Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claims.Subject))
            throw new ArgumentException("Reconciliation requires a non-empty subject.", nameof(claims));

        var identity = await Identity.Get(claims.Subject, ct).ConfigureAwait(false)
                       ?? new Identity { Id = claims.Subject };

        // Backfill display fields from the IdP only when empty — a user-set value is authoritative and never clobbered.
        if (string.IsNullOrWhiteSpace(identity.DisplayName) && !string.IsNullOrWhiteSpace(claims.DisplayName))
            identity.DisplayName = claims.DisplayName;
        if (string.IsNullOrWhiteSpace(identity.Picture) && !string.IsNullOrWhiteSpace(claims.Picture))
            identity.Picture = claims.Picture;

        await identity.Save(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(claims.Email))
            await ReconcileEmailAsync(identity.Id, claims.Email!, claims.EmailVerified, ct).ConfigureAwait(false);

        return identity;
    }

    // P0 trusts the IdP's email_verified assertion as-is (best-effort). The stricter verified-email matching /
    // collision-merge contract — provider provenance, cross-person hijack guards — is the deferred P4 work.
    private static async Task ReconcileEmailAsync(string identityId, string address, bool verified, CancellationToken ct)
    {
        var normalized = address.Trim().ToLowerInvariant();
        var existing = await IdentityEmail.Query(e => e.IdentityId == identityId, ct).ConfigureAwait(false);
        var match = existing.FirstOrDefault(e => string.Equals(e.Address, normalized, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            await new IdentityEmail
            {
                Id = IdentityEmail.KeyFor(identityId, normalized), // deterministic → concurrent first sign-ins converge to one row
                IdentityId = identityId,
                Address = normalized,
                Verified = verified,
                Primary = existing.Count == 0, // the first factor becomes primary
            }.Save(ct).ConfigureAwait(false);
        }
        else if (verified && !match.Verified)
        {
            match.Verified = true;
            await match.Save(ct).ConfigureAwait(false);
        }
    }
}
