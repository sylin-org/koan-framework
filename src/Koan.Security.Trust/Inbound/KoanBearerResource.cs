using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Koan.Security.Trust.Inbound;

/// <summary>
/// SEC-0025 — a bearer accepted for one resource. <c>Koan.bearer</c> proves the token authentic (signature, issuer,
/// algorithm, lifetime); this adds what a resource server decides: the token's audience names this resource, with or
/// without one trailing slash (RFC 8707), and its subject becomes <see cref="ClaimTypes.NameIdentifier"/>, the identifier
/// a signed-in person's session carries, so the person and the client acting for them read as one. Web Auth's resource
/// edge for ordinary requests and the MCP edge share it.
/// </summary>
public static class KoanBearerResource
{
    /// <summary>The RFC 6750 error code for a token that is expired, tampered, foreign or issued for another resource.</summary>
    public const string InvalidToken = "invalid_token";

    private const string AudienceClaim = "aud";
    private const string SubjectClaim = "sub";

    /// <summary>
    /// Authenticate this request's bearer for <paramref name="resource"/>. <see cref="AuthenticateResult.NoResult"/> when
    /// it carries no bearer token; a failure when the token is not authentic, names another audience or names no
    /// subject; otherwise the bearer's success, its principal carrying <see cref="ClaimTypes.NameIdentifier"/>.
    /// </summary>
    public static async Task<AuthenticateResult> Authenticate(HttpContext context, string resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var result = await context.AuthenticateAsync(KoanBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is not { } principal) return result;
        if (!Accepts(principal, resource)) return AuthenticateResult.Fail("The token was not issued for this resource.");
        if (principal.FindFirst(SubjectClaim)?.Value is not { Length: > 0 } subject)
            return AuthenticateResult.Fail("The token names no subject.");

        if (principal.Identity is ClaimsIdentity identity && identity.FindFirst(ClaimTypes.NameIdentifier) is null)
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        return result;
    }

    /// <summary>
    /// True when <paramref name="principal"/>'s audience names <paramref name="resource"/>, with or without one trailing
    /// slash. A credential with no audience is unbound and names no resource. The comparison is ordinal: RFC 7519 makes
    /// <c>aud</c> a case-sensitive string.
    /// </summary>
    public static bool Accepts(ClaimsPrincipal principal, string resource)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var canonical = resource.EndsWith('/') ? resource[..^1] : resource;
        return principal.FindAll(AudienceClaim).Any(audience =>
            string.Equals(audience.Value, canonical, StringComparison.Ordinal)
            || string.Equals(audience.Value, canonical + "/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The RFC 6750 <c>WWW-Authenticate</c> value of a bearer challenge: <c>Bearer</c> with <paramref name="error"/> and its
    /// <paramref name="description"/>, each only when given.
    /// </summary>
    public static string Challenge(string? error = null, string? description = null)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(error)) parts.Add($"error=\"{error}\"");
        if (!string.IsNullOrEmpty(description)) parts.Add($"error_description=\"{description}\"");
        return parts.Count == 0 ? "Bearer" : "Bearer " + string.Join(", ", parts);
    }
}
