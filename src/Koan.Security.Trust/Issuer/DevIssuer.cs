using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 §5 (Tier-0) / §11 — the in-process dev issuer: a per-process, NON-DETERMINISTIC asymmetric
/// (ES256) keypair generated once and held for the process lifetime. Registered as a singleton (every
/// resolve must share the same key, or validation fails non-deterministically). This is the faithful
/// absorb of the Test connector's ES256 <c>JwtTokenService</c> behind the <see cref="IIssuer"/> seam;
/// the original remains until its consumers are rerouted (SEC-0001 Phase 2, 2m).
/// </summary>
public sealed class DevIssuer : IIssuer
{
    private readonly ILogger<DevIssuer> _logger;
    private readonly TrustIssuerOptions _options;
    private readonly ECDsaSecurityKey _signingKey;

    public DevIssuer(IOptions<TrustIssuerOptions> options, ILogger<DevIssuer> logger)
    {
        _logger = logger;
        _options = options.Value;
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = Guid.NewGuid().ToString("n") };
    }

    public string KeyId => _signingKey.KeyId!;
    public string Algorithm => SecurityAlgorithms.EcdsaSha256; // "ES256"
    public string Issuer => _options.Issuer;
    public string Audience => _options.Audience;
    public SecurityKey SignatureKey => _signingKey;

    public string Issue(TrustClaims c, TimeSpan? lifetime = null)
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
        foreach (var permission in c.Permissions) claims.Add(new Claim("Koan.permission", permission));
        if (c.Extra is not null)
            foreach (var kvp in c.Extra)
                foreach (var value in kvp.Value)
                    claims.Add(new Claim(kvp.Key, value));

        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(_options.DefaultLifetimeMinutes)),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(_signingKey, Algorithm),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _signingKey,
        ValidAlgorithms = new[] { Algorithm }, // SEC-0001 §6.1: pin the alg — alg=none / confusion unrepresentable
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };

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
