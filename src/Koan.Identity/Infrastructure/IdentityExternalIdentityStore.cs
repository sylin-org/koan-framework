using Newtonsoft.Json.Linq;
using Koan.Data.Core;
using Koan.Web.Auth.Domain;

namespace Koan.Identity.Infrastructure;

/// <summary>
/// SEC-0007 — the durable <see cref="IExternalIdentityStore"/> backed by <see cref="ExternalIdentityLink"/>.
/// <see cref="Link"/> is the already-wired reconciliation call site (<c>AuthSchemeSeeder</c> calls it during the
/// OAuth callback): it persists the provider → person relation AND ensures the durable <see cref="Identity"/>
/// exists (best-effort reconcile from the userinfo JSON), so signing in materializes the person with no extra
/// wiring. Replaces the in-memory stub.
/// </summary>
internal sealed class IdentityExternalIdentityStore : IExternalIdentityStore
{
    private readonly IIdentityReconciler _reconciler;

    public IdentityExternalIdentityStore(IIdentityReconciler reconciler) => _reconciler = reconciler;

    public async Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default)
    {
        var links = await ExternalIdentityLink.Query(l => l.IdentityId == userId, ct).ConfigureAwait(false);
        return links.Select(Map).ToList();
    }

    public async Task Link(ExternalIdentity identity, CancellationToken ct = default)
    {
        // On one OAuth/OIDC sign-in this fires alongside IdentityAuthFlowHandler.OnSignIn — both reconcile the same
        // subject, sequentially within the request. ReconcileAsync is idempotent so the two passes converge. This
        // path carries the full userinfo JSON (incl. email), so on OAuth2 — where the cookie identity does NOT carry
        // an email claim — Link() is the de-facto email source; the handler covers OIDC and email-stamping providers.
        await _reconciler.ReconcileAsync(ClaimsFrom(identity), ct).ConfigureAwait(false);

        await new ExternalIdentityLink
        {
            Id = ExternalIdentityLink.KeyFor(identity.UserId, identity.Provider, identity.ProviderKeyHash),
            IdentityId = identity.UserId,
            Provider = identity.Provider,
            ProviderKeyHash = identity.ProviderKeyHash,
            ClaimsJson = identity.ClaimsJson,
        }.Save(ct).ConfigureAwait(false);
    }

    public async Task Unlink(string userId, string provider, string providerKeyHash, CancellationToken ct = default)
    {
        var id = ExternalIdentityLink.KeyFor(userId, provider, providerKeyHash);
        var link = await ExternalIdentityLink.Get(id, ct).ConfigureAwait(false);
        if (link is not null) await link.Remove(ct).ConfigureAwait(false);
    }

    private static ExternalIdentity Map(ExternalIdentityLink l) => new()
    {
        UserId = l.IdentityId,
        Provider = l.Provider,
        ProviderKeyHash = l.ProviderKeyHash,
        ClaimsJson = l.ClaimsJson,
        CreatedUtc = l.CreatedAt,
    };

    private static IdentityClaims ClaimsFrom(ExternalIdentity identity)
    {
        string? name = null, picture = null, email = null;
        var emailVerified = false;
        if (!string.IsNullOrWhiteSpace(identity.ClaimsJson))
        {
            try
            {
                var o = JObject.Parse(identity.ClaimsJson);
                name = (string?)o["name"] ?? (string?)o["username"];
                picture = (string?)o["picture"] ?? (string?)o["avatar"];
                email = (string?)o["email"];
                emailVerified = (bool?)o["email_verified"] ?? false;
            }
            catch
            {
                // Best-effort claims parse — a malformed userinfo blob still links the identity.
            }
        }
        return new IdentityClaims(identity.UserId, name, picture, email, emailVerified, identity.Provider);
    }
}
