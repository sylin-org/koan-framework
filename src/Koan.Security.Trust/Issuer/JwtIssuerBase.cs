using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// The shared JWT mint/validate engine behind every <see cref="IIssuer"/>. Subclasses supply only the signing
/// strategy (key + algorithm + validation parameters); the claim projection (<see cref="JwtClaimFactory"/>),
/// token assembly, and alg-pinned validation are identical across tiers. One engine, two key strategies —
/// HS256 <see cref="SharedKeyIssuer"/> (symmetric, service-mesh) and ES256 <see cref="EcdsaIssuer"/>
/// (asymmetric, the AS / user-facing tier, SEC-0001's "whoever can verify must not be able to forge").
/// </summary>
public abstract class JwtIssuerBase : IIssuer
{
    private readonly ILogger _logger;

    protected JwtIssuerBase(ILogger logger) => _logger = logger;

    public abstract string KeyId { get; }
    public abstract string Algorithm { get; }
    public abstract string Issuer { get; }
    public abstract string Audience { get; }
    public abstract SecurityKey SignatureKey { get; }

    /// <summary>The credentials this issuer signs with (key + algorithm).</summary>
    protected abstract SigningCredentials SigningCredentials { get; }

    /// <summary>The default token lifetime when a caller does not specify one.</summary>
    protected abstract TimeSpan DefaultLifetime { get; }

    public string Issue(TrustClaims c, TimeSpan? lifetime = null, string? audience = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(JwtClaimFactory.From(c)),
            Expires = DateTime.UtcNow.Add(lifetime ?? DefaultLifetime),
            Issuer = Issuer,
            // SEC-0006 D2 — honor the per-call audience (RFC 8707); fall back to the configured default.
            Audience = string.IsNullOrWhiteSpace(audience) ? Audience : audience,
            SigningCredentials = SigningCredentials,
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public abstract TokenValidationParameters CreateValidationParameters();

    public bool TryValidate(string token, out ClaimsPrincipal principal)
    {
        principal = default!;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, CreateValidationParameters(), out var validated);
            if (validated is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, Algorithm, StringComparison.OrdinalIgnoreCase))
            {
                // SEC-0001 §6.1 — defence in depth behind ValidAlgorithms: never accept a token whose header
                // algorithm disagrees with this issuer's pinned suite (alg=none / HS↔ES confusion).
                _logger.LogWarning("Trust issuer: token rejected — algorithm mismatch.");
                return false;
            }
            return true;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogDebug("Trust issuer: token validation failed: {Error}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trust issuer: unexpected error during token validation.");
            return false;
        }
    }
}
