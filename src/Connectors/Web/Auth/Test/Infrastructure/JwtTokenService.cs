using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Connector.Test.Infrastructure;

/// <summary>
/// SEC-0001 §11 — the dev TestProvider signs JWTs with a per-process, NON-DETERMINISTIC asymmetric
/// (ES256) key. The previous HS256 path derived a symmetric key from a public formula
/// (<c>issuer:audience:test-provider-key</c>): anyone reading the open-source repo could reconstruct
/// it and forge a "valid" token. Asymmetric signing means a verifier (the future inbound validator /
/// published JWKS) can validate without ever holding a key that can mint — "whoever can verify must
/// not be able to forge." The keypair is generated once (this service is a singleton; see
/// <c>KoanAutoRegistrar</c>) and lives only for the process lifetime.
/// </summary>
public sealed class JwtTokenService
{
    // SEC-0001 §6.1: one fixed asymmetric suite; the verifier pins it (no in-band alg negotiation).
    private const string Algorithm = SecurityAlgorithms.EcdsaSha256; // "ES256"

    private readonly ILogger<JwtTokenService> _logger;
    private readonly ECDsaSecurityKey _signingKey;

    public JwtTokenService(ILogger<JwtTokenService> logger)
    {
        _logger = logger;
        // Per-process ephemeral P-256 keypair — random by construction, not derivable from config.
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = Guid.NewGuid().ToString("n") };
    }

    /// <summary>Key id (<c>kid</c>) of the current signing key — for JWKS publication / rotation.</summary>
    public string KeyId => _signingKey.KeyId!;

    /// <summary>
    /// OIDC <c>id_token</c> (ES256) with iss/aud/sub/nonce — validated by the maintained OpenIdConnectHandler
    /// against the JWKS endpoint. The dev <c>test-oidc</c> provider's signed-token half (WEB-0071 / E5 chunk 4).
    /// </summary>
    public string CreateIdToken(UserProfile profile, string issuer, string audience, string? nonce, TimeSpan ttl)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, profile.Email),
            new(JwtRegisteredClaimNames.Name, profile.Username),
            new(JwtRegisteredClaimNames.Email, profile.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (!string.IsNullOrWhiteSpace(nonce)) claims.Add(new Claim("nonce", nonce!));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(ttl),
            IssuedAt = DateTime.UtcNow,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(_signingKey, Algorithm)
        };
        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    /// <summary>The public half of the signing key as a JWK, for the OIDC JWKS endpoint.</summary>
    public JsonWebKey GetPublicJwk()
    {
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(_signingKey);
        jwk.Use = "sig";
        jwk.Alg = Algorithm; // ES256
        return jwk;
    }

    public string CreateToken(UserProfile profile, DevTokenStore.ClaimEnvelope env, TestProviderOptions options)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, profile.Email),
            new(JwtRegisteredClaimNames.Name, profile.Username),
            new(JwtRegisteredClaimNames.Email, profile.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add roles
        foreach (var role in env.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Add permissions using Koan convention
        foreach (var permission in env.Permissions)
        {
            claims.Add(new Claim("Koan.permission", permission));
        }

        // Add custom claims
        foreach (var kvp in env.Claims)
        {
            foreach (var value in kvp.Value)
            {
                claims.Add(new Claim(kvp.Key, value));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(options.JwtExpirationMinutes),
            Issuer = options.JwtIssuer,
            Audience = options.JwtAudience,
            SigningCredentials = new SigningCredentials(_signingKey, Algorithm)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public bool ValidateToken(string token, TestProviderOptions options, out UserProfile profile, out DevTokenStore.ClaimEnvelope env)
    {
        profile = default!;
        env = new DevTokenStore.ClaimEnvelope();

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                // SEC-0001 §6.1: pin the algorithm; reject alg=none / RS256↔HS256 confusion structurally.
                ValidAlgorithms = new[] { Algorithm },
                ValidateIssuer = true,
                ValidIssuer = options.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = options.JwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1) // Allow 1 minute clock skew
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !string.Equals(jwtToken.Header.Alg, Algorithm, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("TestProvider JWT: invalid algorithm in token");
                return false;
            }

            // Extract profile information - try both JWT standard claim names and Microsoft claim schema URIs
            var emailClaim = principal.FindFirst(JwtRegisteredClaimNames.Email) ??
                            principal.FindFirst(ClaimTypes.Email);
            var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub) ??
                          principal.FindFirst(ClaimTypes.NameIdentifier);
            var nameClaim = principal.FindFirst(JwtRegisteredClaimNames.Name) ??
                           principal.FindFirst(ClaimTypes.Name);

            var email = emailClaim?.Value ?? subClaim?.Value ?? "";
            var username = nameClaim?.Value ?? email;

            profile = new UserProfile(username, email, null);

            // Extract roles
            foreach (var roleClaim in principal.FindAll(ClaimTypes.Role))
            {
                if (!string.IsNullOrWhiteSpace(roleClaim.Value))
                    env.Roles.Add(roleClaim.Value);
            }

            // Extract permissions
            foreach (var permClaim in principal.FindAll("Koan.permission"))
            {
                if (!string.IsNullOrWhiteSpace(permClaim.Value))
                    env.Permissions.Add(permClaim.Value);
            }

            // Extract custom claims (excluding standard JWT claims)
            var excludedTypes = new HashSet<string>
            {
                JwtRegisteredClaimNames.Sub,
                JwtRegisteredClaimNames.Name,
                JwtRegisteredClaimNames.Email,
                JwtRegisteredClaimNames.Jti,
                JwtRegisteredClaimNames.Iat,
                JwtRegisteredClaimNames.Exp,
                JwtRegisteredClaimNames.Iss,
                JwtRegisteredClaimNames.Aud,
                JwtRegisteredClaimNames.Nbf,
                ClaimTypes.Role,
                ClaimTypes.NameIdentifier,
                ClaimTypes.Name,
                ClaimTypes.Email,
                "Koan.permission"
            };

            foreach (var claim in principal.Claims)
            {
                if (!excludedTypes.Contains(claim.Type) && !string.IsNullOrWhiteSpace(claim.Value))
                {
                    if (!env.Claims.TryGetValue(claim.Type, out var list))
                    {
                        env.Claims[claim.Type] = list = new List<string>();
                    }
                    if (!list.Contains(claim.Value))
                    {
                        list.Add(claim.Value);
                    }
                }
            }

            return true;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogDebug("TestProvider JWT: token validation failed: {Error}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestProvider JWT: unexpected error during token validation");
            return false;
        }
    }
}
