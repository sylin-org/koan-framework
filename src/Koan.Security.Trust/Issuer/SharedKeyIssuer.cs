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
/// rungs; the asymmetric <see cref="EcdsaIssuer"/> (ES256) is the user-facing / authorization-server tier
/// where the verifier must never be able to forge (SEC-0006 D1).
/// </para>
/// </summary>
public sealed class SharedKeyIssuer : JwtIssuerBase
{
    private readonly TrustIssuerOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public SharedKeyIssuer(IOptions<TrustIssuerOptions> options, ILogger<SharedKeyIssuer> logger)
        : base(logger)
    {
        _options = options.Value;
        // SHA-256(secret) → a uniform 256-bit HMAC key: valid for any secret length, and deterministic so all
        // holders of the same secret derive the same key (mutual validation + cross-service self-mint).
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_options.Key));
        _signingKey = new SymmetricSecurityKey(keyBytes)
        {
            KeyId = Convert.ToHexString(keyBytes)[..8].ToLowerInvariant(),
        };
    }

    public override string KeyId => _signingKey.KeyId!;
    public override string Algorithm => SecurityAlgorithms.HmacSha256; // "HS256"
    public override string Issuer => _options.Issuer;
    public override string Audience => _options.Audience;
    public override SecurityKey SignatureKey => _signingKey;

    protected override SigningCredentials SigningCredentials => new(_signingKey, Algorithm);
    protected override TimeSpan DefaultLifetime => TimeSpan.FromMinutes(_options.DefaultLifetimeMinutes);

    public override TokenValidationParameters CreateValidationParameters() => new()
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
}
