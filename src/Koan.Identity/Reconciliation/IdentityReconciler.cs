using Microsoft.Extensions.Options;
using Koan.Data.Core;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// The idempotent reconciliation core (SEC-0007 P0). Upserts the durable <see cref="Identity"/> keyed on the
/// claims subject and attaches the verified-email factor. Never clobbers user-owned display fields with an IdP
/// backfill. Pure data operations over <c>Entity&lt;T&gt;</c> — no ambient reads.
/// </summary>
internal sealed class IdentityReconciler : IIdentityReconciler
{
    private readonly bool _autoMerge;

    // DI injects the options; the dev-seed news this up directly (merge off — it seeds distinct users).
    public IdentityReconciler(IOptions<IdentityOptions>? options = null)
        => _autoMerge = options?.Value.AutoMergeVerifiedEmail ?? false;

    public async Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claims.Subject))
            throw new ArgumentException("Reconciliation requires a non-empty subject.", nameof(claims));

        // Resolve the canonical person (SEC-0007 D5, person ≠ email): use this sub if it is already a person;
        // otherwise — ONLY when auto-merge is explicitly enabled (off by default; unsafe without a provider-trust
        // policy, the ADR open question) — MERGE onto an Active person that already holds the SAME VERIFIED email,
        // so a second IdP for the same human links onto the existing person instead of spawning a duplicate.
        var identity = await Identity.Get(claims.Subject, ct).ConfigureAwait(false);
        if (identity is null && _autoMerge && claims.EmailVerified && !string.IsNullOrWhiteSpace(claims.Email))
            identity = await FindByVerifiedEmailAsync(claims.Email!, ct).ConfigureAwait(false);
        identity ??= new Identity { Id = claims.Subject };

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

    // The single Active person holding this VERIFIED email, or null. Refuses to guess: 0 matches → no merge; >1
    // distinct verified holder (ambiguous / data inconsistency) → no merge; a non-Active holder → no merge (never
    // resurrect a deprovisioned account). Caller already gated on the explicit opt-in + a verified incoming email.
    private static async Task<Identity?> FindByVerifiedEmailAsync(string email, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        var matches = await IdentityEmail.Query(e => e.Address == normalized, ct).ConfigureAwait(false);
        var verifiedHolders = matches.Where(e => e.Verified).Select(e => e.IdentityId).Distinct().ToList();
        if (verifiedHolders.Count != 1) return null;
        var person = await Identity.Get(verifiedHolders[0], ct).ConfigureAwait(false);
        return person is { IsActive: true } ? person : null;
    }

    /// <summary>The one canonical email normalization, shared by the matcher and the factor writer.</summary>
    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static async Task ReconcileEmailAsync(string identityId, string address, bool verified, CancellationToken ct)
    {
        var normalized = NormalizeEmail(address);
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
