using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 §5 / SEC-0003 §2.4 — the shared-secret issuer. Signs <b>HS256</b> with a 256-bit key derived as
/// <c>SHA-256(<see cref="TrustIssuerOptions.Key"/>)</c>. Because the key defaults to the well-known insecure
/// value, every service self-mints valid tokens with zero config, and services sharing the key (same box, or
/// different boxes with the same value) trust each other's tokens. Registered as a singleton.
/// <para>
/// Symmetric "whoever holds the key can mint" is the accepted trade-off for the dev / small-trusted-team
/// rungs; the per-node asymmetric fleet tier (SEC-0001 Rung 2) elects in when a real issuer is configured.
/// </para>
/// </summary>
public sealed class SharedKeyIssuer : IIssuer
{
    private readonly ILogger<SharedKeyIssuer> _logger;
    private readonly TrustIssuerOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public SharedKeyIssuer(IOptions<TrustIssuerOptions> options, ILogger<SharedKeyIssuer> logger)
    {
        _logger = logger;
        _options = options.Value;
        // SHA-256(secret) → a uniform 256-bit HMAC key: valid for any secret length, and deterministic so all
        // holders of the same secret derive the same key (mutual validation + cross-service self-mint).
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_options.Key));
        _signingKey = new SymmetricSecurityKey(keyBytes)
        {
            KeyId = Convert.ToHexString(keyBytes)[..8].ToLowerInvariant(),
        };
    }

    public string KeyId => _signingKey.KeyId!;
    public string Algorithm => SecurityAlgorithms.HmacSha256; // "HS256"
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
