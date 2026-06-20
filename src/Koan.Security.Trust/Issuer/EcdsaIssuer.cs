using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 / SEC-0006 D1 — the asymmetric (ES256) issuer: the user-facing / Authorization-Server tier where
/// "whoever can verify must not be able to forge" is non-negotiable. Signs with the active P-256 key from
/// the injected <see cref="IIssuerKeyStore"/>; publishes only public JWKs. Token <c>iss</c> and the default
/// <c>aud</c> come from <see cref="TrustIssuerOptions"/> (shared with the HS256 tier so the wire shape is
/// uniform), but every call may bind a specific resource audience (SEC-0006 D2 / RFC 8707).
/// <para>
/// Validation is <b>audience-agnostic</b> here by design: this issuer asserts authenticity (signature, issuer,
/// algorithm, lifetime); the per-resource <c>aud</c> check belongs to the resource server (the MCP edge),
/// which is the SEC-0006 D2 confused-deputy fix. A symmetric issuer's fixed-audience check is exactly the
/// bug being removed.
/// </para>
/// </summary>
public sealed class EcdsaIssuer : JwtIssuerBase, IAsymmetricIssuer
{
    private readonly IIssuerKeyStore _keyStore;
    private readonly TrustIssuerOptions _options;

    public EcdsaIssuer(IIssuerKeyStore keyStore, IOptions<TrustIssuerOptions> options, ILogger<EcdsaIssuer> logger)
        : base(logger)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _options = options.Value;
    }

    private EcdsaKeyRing Ring => _keyStore.GetKeyRing();

    public override string KeyId => Ring.Active.KeyId!;
    public override string Algorithm => EcdsaKeyRing.Algorithm; // "ES256"
    public override string Issuer => _options.Issuer;
    public override string Audience => _options.Audience;
    public override SecurityKey SignatureKey => Ring.Active;

    protected override SigningCredentials SigningCredentials => new(Ring.Active, Algorithm);
    protected override TimeSpan DefaultLifetime => TimeSpan.FromMinutes(_options.DefaultLifetimeMinutes);

    public IReadOnlyList<JsonWebKey> PublishedKeys => Ring.ToPublicJwks();

    public override TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        // Accept every key in the ring (active + retiring) so a rotated key keeps validating until its last
        // token expires (JWKS overlap).
        IssuerSigningKeys = Ring.All,
        ValidAlgorithms = new[] { Algorithm }, // SEC-0001 §6.1: pin ES256 — alg=none / HS↔ES confusion unrepresentable
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        // SEC-0006 D2 — audience is enforced per-resource at the resource server, not here.
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}
