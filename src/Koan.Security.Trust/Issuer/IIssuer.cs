using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// Trust's one ES256 token contract: mint short-lived credentials, expose public verification keys, and
/// provide the alg-pinned parameters used by the inbound bearer scheme.
/// </summary>
public interface IIssuer
{
    /// <summary>
    /// Public keys to publish at a JWKS endpoint: the active key followed by retiring keys still required for
    /// overlap. Private components are never present.
    /// </summary>
    IReadOnlyList<JsonWebKey> PublishedKeys { get; }

    /// <summary>
    /// Mint a credential carrying <paramref name="claims"/>, valid for <paramref name="lifetime"/> (or the
    /// configured default), bound to <paramref name="audience"/> (or the configured default audience).
    /// <para>
    /// SEC-0006 D2 (RFC 8707) — the per-call audience binds a token to a specific resource. A resource server
    /// validates <c>aud == its own resource id</c>, so a token minted for resource A cannot be replayed against
    /// resource B (the confused-deputy fix). Pass <c>null</c> to mint against the issuer's default audience.
    /// </para>
    /// </summary>
    string Issue(TrustClaims claims, TimeSpan? lifetime = null, string? audience = null);

    /// <summary>The current alg-pinned, public-key validation parameters for this issuer.</summary>
    TokenValidationParameters CreateValidationParameters();

    /// <summary>Validate a token; on success yields the principal. Never throws.</summary>
    bool TryValidate(string token, out ClaimsPrincipal principal);
}
