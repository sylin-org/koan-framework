using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// Trust's ES256 issuer. It signs with the active P-256 key from <see cref="IIssuerKeyStore"/>, publishes
/// only public JWKs, and accepts retiring verification keys during rotation overlap.
/// <para>
/// One issuer name (SEC-0023): the one <see cref="IIssuerIdentifier"/> states for the current request when an owner
/// above Trust registers it (the embedded authorization server's, so a token's <c>iss</c> is the issuer its metadata
/// publishes), else <see cref="TrustIssuerOptions.Issuer"/>, else <c>koan-dev</c>. Validation accepts the stated name and
/// the configured-or-default one, which a token minted where nothing can be stated carries.
/// </para>
/// <para>
/// Validation is <b>audience-agnostic</b> here by design: this issuer asserts authenticity (signature, issuer,
/// algorithm, lifetime); the per-resource <c>aud</c> check belongs to the resource server (the MCP edge),
/// which prevents a valid token for one resource from being replayed against another.
/// </para>
/// </summary>
internal sealed class EcdsaIssuer : IIssuer
{
    private readonly IIssuerKeyStore _keyStore;
    private readonly TrustIssuerOptions _options;
    private readonly ILogger<EcdsaIssuer> _logger;
    private readonly IIssuerIdentifier? _identifier;

    public EcdsaIssuer(IIssuerKeyStore keyStore, IOptions<TrustIssuerOptions> options, ILogger<EcdsaIssuer> logger,
        IIssuerIdentifier? identifier = null)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _options = options.Value;
        _logger = logger;
        _identifier = identifier;
    }

    private EcdsaKeyRing Ring => _keyStore.GetKeyRing();

    /// <summary>The name a token minted where nothing above Trust states one carries; always accepted.</summary>
    private string Named => _options.Issuer ?? Infrastructure.Constants.Issuer.Fallback;

    /// <summary>The issuer this application's tokens carry for the current request.</summary>
    internal string Current => _identifier?.Resolve() ?? Named;

    public IReadOnlyList<JsonWebKey> PublishedKeys => Ring.ToPublicJwks();

    public string Issue(TrustClaims claims, TimeSpan? lifetime = null, string? audience = null)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(JwtClaimFactory.From(claims)),
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(_options.DefaultLifetimeMinutes)),
            Issuer = Current,
            Audience = string.IsNullOrWhiteSpace(audience) ? _options.Audience : audience,
            SigningCredentials = new SigningCredentials(Ring.Active, EcdsaKeyRing.Algorithm),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        // Accept every key in the ring (active + retiring) so a rotated key keeps validating until its last
        // token expires (JWKS overlap).
        IssuerSigningKeys = Ring.All,
        ValidAlgorithms = [EcdsaKeyRing.Algorithm],
        ValidateIssuer = true,
        ValidIssuer = Named,
        // SEC-0023 — the stated issuer is decided per request (it may derive from the request), so it is read at
        // validation time rather than captured here.
        IssuerValidator = AcceptIssuer,
        // SEC-0006 D2 — audience is enforced per-resource at the resource server, not here.
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    private string AcceptIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (string.Equals(issuer, Named, StringComparison.Ordinal)) return issuer;
        if (_identifier?.Resolve() is { } stated && string.Equals(issuer, stated, StringComparison.Ordinal)) return issuer;
        throw new SecurityTokenInvalidIssuerException($"The token's issuer '{issuer}' is not this application's.") { InvalidIssuer = issuer };
    }

    public bool TryValidate(string token, out ClaimsPrincipal principal)
    {
        principal = default!;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, CreateValidationParameters(), out var validated);
            if (validated is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, EcdsaKeyRing.Algorithm, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Trust issuer rejected a token with an unexpected signing algorithm.");
                return false;
            }

            return true;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogDebug("Trust issuer rejected a token: {Error}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trust issuer encountered an unexpected token-validation error.");
            return false;
        }
    }
}
