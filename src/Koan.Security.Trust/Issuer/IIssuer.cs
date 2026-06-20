using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 — the trust fabric's token issuer. Mints short-lived, asymmetric (ES256) credentials and
/// exposes the verification parameters its tokens validate against, so the inbound bearer scheme (2d)
/// and any peer verifier share one alg-pinned, public-key validation contract. A verifier holds only
/// the public key and can never mint — "whoever can verify must not be able to forge."
/// </summary>
public interface IIssuer
{
    /// <summary>Key id (<c>kid</c>) of the active signing key — for JWKS publication / rotation.</summary>
    string KeyId { get; }

    /// <summary>The signing algorithm used by this issuer (ES256 for the asymmetric tier, HS256 for the service-mesh tier).</summary>
    string Algorithm { get; }

    /// <summary>Token issuer (<c>iss</c>).</summary>
    string Issuer { get; }

    /// <summary>Token audience (<c>aud</c>).</summary>
    string Audience { get; }

    /// <summary>
    /// The signature key. For the asymmetric tier this is the public verification key (safe to publish as JWKS);
    /// for the symmetric service-mesh tier it is the shared secret itself and MUST NOT be published.
    /// </summary>
    SecurityKey SignatureKey { get; }

    /// <summary>
    /// Mint a credential carrying <paramref name="claims"/>, valid for <paramref name="lifetime"/> (or the
    /// configured default), bound to <paramref name="audience"/> (or the configured default <see cref="Audience"/>).
    /// <para>
    /// SEC-0006 D2 (RFC 8707) — the per-call audience binds a token to a specific resource. A resource server
    /// validates <c>aud == its own resource id</c>, so a token minted for resource A cannot be replayed against
    /// resource B (the confused-deputy fix). Pass <c>null</c> to mint against the issuer's default audience.
    /// </para>
    /// </summary>
    string Issue(TrustClaims claims, TimeSpan? lifetime = null, string? audience = null);

    /// <summary>The alg-pinned, public-key validation parameters this issuer's tokens validate against.</summary>
    TokenValidationParameters CreateValidationParameters();

    /// <summary>Validate a token; on success yields the principal. Never throws.</summary>
    bool TryValidate(string token, out ClaimsPrincipal principal);
}
