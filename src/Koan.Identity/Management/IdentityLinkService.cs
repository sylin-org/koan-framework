using Koan.Data.Core;
using Koan.Identity.Reconciliation;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 D5 — <b>explicit, session-authorized account-linking</b> (the takeover-safe person ≠ email model,
/// architect-chosen over email auto-merge). A signed-in user attaches a second provider identity to THEIR person;
/// thereafter that provider's sign-ins resolve to the same canonical person (via <c>IdentityReconciler</c>'s
/// link anchor). The authorization is the existing session — never a fresh IdP's email claim — so a low-assurance
/// or attacker IdP cannot acquire someone else's account.
/// </summary>
public sealed class IdentityLinkService
{
    /// <summary>Link a provider identity (<paramref name="provider"/>, <paramref name="providerSub"/>) to <paramref name="personId"/>.</summary>
    public async Task<ExternalIdentityLink> LinkAsync(string personId, string provider, string providerSub, string? claimsJson = null, CancellationToken ct = default)
    {
        var keyHash = IdentityReconciler.ProviderKeyHash(providerSub);
        var link = new ExternalIdentityLink
        {
            Id = ExternalIdentityLink.KeyFor(providerSub, provider, keyHash),
            IdentityId = personId,
            Provider = provider,
            ProviderKeyHash = keyHash,
            ClaimsJson = claimsJson,
        };
        return await link.Save(ct).ConfigureAwait(false);
    }

    /// <summary>List the provider identities linked to a person (the "connected accounts" view).</summary>
    public Task<IReadOnlyList<ExternalIdentityLink>> ForPersonAsync(string personId, CancellationToken ct = default)
        => ExternalIdentityLink.Query(l => l.IdentityId == personId, ct);

    /// <summary>Unlink a connected account by its link id — only the owner may (the link must belong to <paramref name="personId"/>).</summary>
    public async Task<bool> UnlinkAsync(string personId, string linkId, CancellationToken ct = default)
    {
        var link = await ExternalIdentityLink.Get(linkId, ct).ConfigureAwait(false);
        if (link is null || link.IdentityId != personId) return false; // never unlink someone else's link
        await link.Remove(ct).ConfigureAwait(false);
        return true;
    }
}
