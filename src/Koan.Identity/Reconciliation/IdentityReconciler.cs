using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Identity.Reconciliation;

/// <summary>
/// The idempotent reconciliation core (SEC-0007 P0, SEC-0040). Upserts the durable <see cref="Identity"/> a sign-in
/// arrives as and attaches the verified-email factor. Never clobbers user-owned display fields with an IdP backfill.
/// <para>
/// <b>A person is Koan's own.</b> A provider's sign-in resolves its login — the <see cref="ExternalIdentityLink"/> for this
/// (provider, sub) — to the person it is bound to. A login seen for the first time is written only if absent, bound to a
/// new person whose id is a GUID v7, so two concurrent first sign-ins of one login end on one person: the loser of the
/// write reads the winner's. Nothing is ever matched by email (an account-takeover vector); a second login is bound only
/// by the signed-in person, through <c>IdentityLinkService.Bind</c>.
/// </para>
/// </summary>
internal sealed class IdentityReconciler : IIdentityReconciler
{
    public async Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claims.Subject))
            throw new ArgumentException("Reconciliation requires a non-empty subject.", nameof(claims));

        var personId = string.IsNullOrWhiteSpace(claims.Provider)
            ? claims.Subject
            : (await Login(claims.Subject, claims.Provider, claims.ClaimsJson, ct).ConfigureAwait(false)).IdentityId;
        var existing = await Identity.Get(personId, ct).ConfigureAwait(false);
        var identity = existing ?? new Identity { Id = personId };
        var changed = existing is null;

        // Backfill display fields from the IdP only when empty — a user-set value is authoritative and never clobbered.
        if (string.IsNullOrWhiteSpace(identity.DisplayName) && !string.IsNullOrWhiteSpace(claims.DisplayName))
        {
            identity.DisplayName = claims.DisplayName;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(identity.Picture) && !string.IsNullOrWhiteSpace(claims.Picture))
        {
            identity.Picture = claims.Picture;
            changed = true;
        }

        // Save-if-changed: a returning person with nothing to backfill is NOT re-written, so a concurrent lifecycle
        // write (e.g. a just-issued Deactivation that flips Status) cannot be reverted by a stale
        // full-row replace from this sign-in path. The reconciler never sets Status, so it can only ever lose a
        // lifecycle change via that clobber — skipping the redundant write removes the race in the common case.
        // Two first sign-ins of one login write one row: they name the same person.
        if (changed)
            await identity.Save(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(claims.Email))
            await ReconcileEmailAsync(identity.Id, claims.Email!, claims.EmailVerified, ct).ConfigureAwait(false);

        return identity;
    }

    /// <summary>
    /// The login (<paramref name="provider"/>, <paramref name="subject"/>): the one bound to its person, or, the first
    /// time it is seen, one bound to a new person. Its userinfo follows the provider's latest word, written only while the
    /// login is still bound to the person it was read with, so a sign-in never brings back a login unbound meanwhile.
    /// </summary>
    internal static async Task<ExternalIdentityLink> Login(string subject, string provider, string? claimsJson, CancellationToken ct)
    {
        var keyHash = ProviderKeyHash(subject);
        var id = ExternalIdentityLink.KeyFor(subject, provider, keyHash);
        var link = await ExternalIdentityLink.Get(id, ct).ConfigureAwait(false);
        if (link is null)
        {
            var first = new ExternalIdentityLink
            {
                Id = id,
                IdentityId = new Identity().Id, // a new person's id is Koan's own: the entity's GUID v7
                Provider = provider,
                ProviderKeyHash = keyHash,
                ClaimsJson = claimsJson,
            };
            var written = await ExternalIdentityLink.Insert(first, ct: ct).ConfigureAwait(false);
            if (written.Outcome == MutationOutcome.Inserted) return first;
            link = await ExternalIdentityLink.Get(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A login written by a concurrent first sign-in could not be read back; sign in again.");
        }

        if (claimsJson is not null && !string.Equals(claimsJson, link.ClaimsJson, StringComparison.Ordinal))
        {
            var person = link.IdentityId;
            link.ClaimsJson = claimsJson;
            await ExternalIdentityLink.ReplaceIf(link, stored => stored.IdentityId == person, ct: ct).ConfigureAwait(false);
        }
        return link;
    }

    /// <summary>The provider-key hash convention (matches Koan.Web.Auth's AuthSchemeSeeder): hex(SHA-256(sub)).</summary>
    internal static string ProviderKeyHash(string subject)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));

    /// <summary>The one canonical email normalization, shared by the factor writer (and any matcher) — see <see cref="IdentityEmail.Normalize"/>.</summary>
    internal static string NormalizeEmail(string email) => IdentityEmail.Normalize(email);

    private static async Task ReconcileEmailAsync(string identityId, string address, bool verified, CancellationToken ct)
    {
        var normalized = NormalizeEmail(address);
        var key = IdentityEmail.KeyFor(identityId, normalized);
        var match = await IdentityEmail.Get(key, ct).ConfigureAwait(false);

        if (match is null)
        {
            // Only a factor not seen before asks whether the person holds another, which one row answers.
            var another = await IdentityEmail.Query(e => e.IdentityId == identityId, new QueryDefinition().WithPagination(1, 1), ct).ConfigureAwait(false);
            await new IdentityEmail
            {
                Id = key, // deterministic → concurrent first sign-ins converge to one row
                IdentityId = identityId,
                Address = normalized,
                Verified = verified,
                Primary = another.Count == 0, // the first factor becomes primary
            }.Save(ct).ConfigureAwait(false);
        }
        else if (verified && !match.Verified)
        {
            match.Verified = true;
            await match.Save(ct).ConfigureAwait(false);
        }
    }
}
