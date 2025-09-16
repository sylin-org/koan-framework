using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Koan.Web.Auth.TestProvider.Options;

namespace Koan.Web.Auth.TestProvider.Infrastructure;

public sealed class JwtTokenService
{
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(ILogger<JwtTokenService> logger)
    {
        _logger = logger;
    }

    public string CreateToken(UserProfile profile, DevTokenStore.ClaimEnvelope env, TestProviderOptions options)
    {
        var signingKey = GetSigningKey(options);
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
            SigningCredentials = new SigningCredentials(signingKey, options.JwtAlgorithm)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwt = tokenHandler.WriteToken(token);


        return jwt;
    }

    public bool ValidateToken(string token, TestProviderOptions options, out UserProfile profile, out DevTokenStore.ClaimEnvelope env)
    {
        profile = default!;
        env = new DevTokenStore.ClaimEnvelope();

        try
        {
            var signingKey = GetSigningKey(options);
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = options.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = options.JwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1) // Allow 1 minute clock skew
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !string.Equals(jwtToken.Header.Alg, options.JwtAlgorithm, StringComparison.OrdinalIgnoreCase))
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

            var email = emailClaim?.Value ?? subClaim?.Value ?? string.Empty;
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

    private static SymmetricSecurityKey GetSigningKey(TestProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            try
            {
                var keyBytes = Convert.FromBase64String(options.JwtSigningKey);
                return new SymmetricSecurityKey(keyBytes);
            }
            catch
            {
                // Fall through to auto-generation
            }
        }

        // Auto-generate a key for development (deterministic based on other config)
        var keyMaterial = $"{options.JwtIssuer}:{options.JwtAudience}:test-provider-key";
        var generatedKeyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return new SymmetricSecurityKey(generatedKeyBytes);
    }
}