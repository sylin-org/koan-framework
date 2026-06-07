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

    /// <summary>The signing algorithm (always asymmetric; ES256 in the dev tier).</summary>
    string Algorithm { get; }

    /// <summary>Token issuer (<c>iss</c>).</summary>
    string Issuer { get; }

    /// <summary>Token audience (<c>aud</c>).</summary>
    string Audience { get; }

    /// <summary>The (public) signature key, for JWKS / peer verification.</summary>
    SecurityKey SignatureKey { get; }

    /// <summary>Mint a credential carrying <paramref name="claims"/>, valid for <paramref name="lifetime"/> (or the configured default).</summary>
    string Issue(TrustClaims claims, TimeSpan? lifetime = null);

    /// <summary>The alg-pinned, public-key validation parameters this issuer's tokens validate against.</summary>
    TokenValidationParameters CreateValidationParameters();

    /// <summary>Validate a token; on success yields the principal. Never throws.</summary>
    bool TryValidate(string token, out ClaimsPrincipal principal);
}
