using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// The idempotent reconciliation core (SEC-0007 P0). Upserts the durable <see cref="Identity"/> for a sign-in and
/// attaches the verified-email factor. Never clobbers user-owned display fields with an IdP backfill.
/// <para>
/// <b>Person ≠ email is realized by EXPLICIT, session-authorized account-linking — never email auto-merge</b>
/// (architect decision: trusting an IdP's <c>email_verified</c> to merge is an account-takeover vector). A sign-in
/// resolves to the canonical person via the durable <see cref="ExternalIdentityLink"/> for this (provider, sub): if
/// the provider identity has been explicitly linked to a person, that person is canonical; otherwise this sub is its
/// own person. A second provider is attached only by a signed-in user via <c>IdentityLinkService</c>.
/// </para>
/// </summary>
internal sealed class IdentityReconciler : IIdentityReconciler
{
    public async Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claims.Subject))
            throw new ArgumentException("Reconciliation requires a non-empty subject.", nameof(claims));

        var canonicalId = await ResolveLinkedIdentityIdAsync(claims.Subject, claims.Provider, ct).ConfigureAwait(false)
                          ?? claims.Subject;
        var identity = await Identity.Get(canonicalId, ct).ConfigureAwait(false) ?? new Identity { Id = canonicalId };

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

    /// <summary>The canonical person id this (provider, sub) is explicitly linked to, or null (a fresh/own identity).</summary>
    internal static async Task<string?> ResolveLinkedIdentityIdAsync(string subject, string? provider, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var keyHash = ProviderKeyHash(subject);
        var link = await ExternalIdentityLink.Get(ExternalIdentityLink.KeyFor(subject, provider, keyHash), ct).ConfigureAwait(false);
        return link?.IdentityId;
    }

    /// <summary>The provider-key hash convention (matches Koan.Web.Auth's AuthSchemeSeeder): hex(SHA-256(sub)).</summary>
    internal static string ProviderKeyHash(string subject)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));

    /// <summary>The one canonical email normalization, shared by the factor writer (and any matcher).</summary>
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
