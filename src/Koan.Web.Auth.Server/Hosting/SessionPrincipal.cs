using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Koan.Security.Trust.Issuer;

namespace Koan.Web.Auth.Server.Hosting;

/// <summary>
/// SEC-0006 — the session → token bridge: projects the cookie session principal (the human leg, owned by
/// <c>Koan.Web.Auth</c>) into the <see cref="TrustClaims"/> the asymmetric issuer mints from. Identity + roles
/// flow straight through (roles live in the session claims); <c>client_id</c> is recorded in <c>Extra</c>.
/// This is the one place the principal shape is read, shared by the dev-token endpoint now and the real
/// <c>/oauth/token</c> endpoint in a later phase.
/// </summary>
internal static class SessionPrincipal
{
    /// <summary>
    /// Project the cookie session principal into trust claims. <paramref name="scopesOverride"/> /
    /// <paramref name="rolesOverride"/> are the dev-token convenience knobs — when supplied they replace the
    /// session's scopes / roles outright (minted as-is, no held-filter — a Development-only testing affordance).
    /// </summary>
    public static TrustClaims ToTrustClaims(ClaimsPrincipal user, string clientId,
        IReadOnlyCollection<string>? scopesOverride = null, IReadOnlyCollection<string>? rolesOverride = null)
    {
        var subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Session principal has no subject claim (sub / NameIdentifier).");

        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var roles = rolesOverride ?? user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        var scopes = scopesOverride ?? user.FindAll(JwtClaimFactory.ScopeClaimType)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal).ToArray();

        // The resource is bound via the token's `aud` (RFC 8707) at issue time — no redundant `resource` claim.
        return new TrustClaims
        {
            Subject = subject,
            Name = name,
            Email = email,
            Roles = roles,
            Scopes = scopes,
            Extra = new Dictionary<string, IReadOnlyList<string>> { ["client_id"] = new[] { clientId } },
        };
    }
}
