using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// Builds the JWT claim list for a <see cref="TrustClaims"/> — the single, shared projection used by every
/// <see cref="IIssuer"/> (HS256 <see cref="SharedKeyIssuer"/> and ES256 <see cref="EcdsaIssuer"/>), so the
/// wire shape of a Koan credential is identical regardless of the signing tier. Keeping claim types exactly
/// as written here (no short-name remap) is what lets the inbound bearer scheme — which sets
/// <c>MapInboundClaims=false</c> — preserve <see cref="ClaimTypes.Role"/> and <c>Koan.permission</c> for
/// the SEC-0004/0005 authorization chain.
/// </summary>
public static class JwtClaimFactory
{
    /// <summary>The permission claim type (no authorization effect on its own — SEC-0001 §8). The canonical
    /// wire-format constant shared by every issuer and any consumer that projects a principal into trust claims.</summary>
    public const string PermissionClaimType = "Koan.permission";

    public static List<Claim> From(TrustClaims c)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, c.Subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (!string.IsNullOrWhiteSpace(c.Name)) claims.Add(new Claim(JwtRegisteredClaimNames.Name, c.Name));
        if (!string.IsNullOrWhiteSpace(c.Email)) claims.Add(new Claim(JwtRegisteredClaimNames.Email, c.Email));
        foreach (var role in c.Roles) claims.Add(new Claim(ClaimTypes.Role, role));
        foreach (var permission in c.Permissions) claims.Add(new Claim(PermissionClaimType, permission));
        if (c.Extra is not null)
            foreach (var kvp in c.Extra)
                foreach (var value in kvp.Value)
                    claims.Add(new Claim(kvp.Key, value));
        return claims;
    }
}
