using System.Security.Claims;
using Newtonsoft.Json.Linq;
using Koan.Identity.Impersonation;
using Koan.Identity.Management;
using Koan.Web.Auth.Domain;

namespace Koan.Identity.Infrastructure;

/// <summary>
/// SEC-0007, SEC-0040 — the durable <see cref="IExternalIdentityStore"/> backed by <see cref="ExternalIdentityLink"/>.
/// Web Auth hands it every verified login at the callback, before the cookie is signed in: <see cref="Link"/> makes the
/// login arrive as the person it is bound to, made on first sight, with the email the userinfo carries; <see cref="Bind"/>
/// binds it to the signed-in person instead. So the sign-in that follows finds its person, and no stray person is made.
/// </summary>
internal sealed class IdentityExternalIdentityStore : IExternalIdentityStore
{
    private readonly IIdentityReconciler _reconciler;
    private readonly IdentityLinkService _links;

    public IdentityExternalIdentityStore(IIdentityReconciler reconciler, IdentityLinkService links)
    {
        _reconciler = reconciler;
        _links = links;
    }

    public async Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default)
        => (await _links.ForPerson(userId, ct).ConfigureAwait(false)).Select(Map).ToList();

    public async Task Link(ExternalIdentity identity, CancellationToken ct = default)
    {
        // On one OAuth/OIDC sign-in this fires before IdentityAuthFlowHandler.OnSignIn — both reconcile the same login,
        // sequentially within the request, and the second finds the person the first made. This path carries the full
        // userinfo JSON (incl. email), so on OAuth2 — where the cookie identity does NOT carry an email claim — Link() is
        // the de-facto email source; the handler covers OIDC and email-stamping providers.
        await _reconciler.ReconcileAsync(ClaimsFrom(identity), ct).ConfigureAwait(false);
    }

    public async Task<BindOutcome> Bind(ClaimsPrincipal signedIn, ExternalIdentity identity, CancellationToken ct = default)
    {
        // SEC-0007 D8: an operator acting as the person never binds a login of their own to them.
        if (ImpersonationClaims.IsImpersonating(signedIn)) return BindOutcome.NotThePerson;
        var person = signedIn.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? signedIn.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(person)) return BindOutcome.NotThePerson;
        var outcome = await _links.Bind(person, identity.Provider, SubjectOf(identity), identity.ClaimsJson, ct).ConfigureAwait(false);
        // The bound login now arrives as the person, carrying what its userinfo says, as a sign-in with it would.
        if (outcome == BindOutcome.Bound)
            await _reconciler.ReconcileAsync(ClaimsFrom(identity), ct).ConfigureAwait(false);
        return outcome;
    }

    private static ExternalIdentity Map(ExternalIdentityLink l) => new()
    {
        Provider = l.Provider,
        ProviderKeyHash = l.ProviderKeyHash,
        ClaimsJson = l.ClaimsJson,
        CreatedUtc = l.CreatedAt,
    };

    private static string SubjectOf(ExternalIdentity identity)
        => string.IsNullOrEmpty(identity.Subject)
            ? throw new ArgumentException("A login arriving from a sign-in carries its provider's subject.", nameof(identity))
            : identity.Subject;

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
        return new IdentityClaims(SubjectOf(identity), name, picture, email, emailVerified, identity.Provider, identity.ClaimsJson);
    }
}
