using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust.Inbound;

/// <summary>
/// SEC-0001 §6.1/§11 — registers the inbound bearer scheme that validates KSVIDs against the trust fabric's
/// issuers with the algorithm pinned. Additive and non-default: cookie remains the default scheme; bearer is
/// opted into per endpoint. Called by <c>Koan.Web.Auth.AddKoanWebAuth()</c> alongside the cookie scheme —
/// Trust never references Web.Auth back up.
/// </summary>
public static class KoanBearerExtensions
{
    public static AuthenticationBuilder AddKoanBearer(this AuthenticationBuilder builder)
    {
        builder.AddJwtBearer(KoanBearerDefaults.AuthenticationScheme, _ => { });
        // The issuers are singletons resolved at options-configuration time; their public keys + iss + alg are
        // the single validation contract the scheme enforces.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, ConfigureKoanBearerOptions>());
        return builder;
    }
}

internal sealed class ConfigureKoanBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IIssuer _issuer;
    private readonly IEnumerable<IAsymmetricIssuer> _asymmetricIssuers;

    public ConfigureKoanBearerOptions(IIssuer issuer, IEnumerable<IAsymmetricIssuer> asymmetricIssuers)
    {
        _issuer = issuer;
        _asymmetricIssuers = asymmetricIssuers;
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, KoanBearerDefaults.AuthenticationScheme, StringComparison.Ordinal)) return;
        // Keep claim types exactly as the issuer wrote them (no short-name remap), so ClaimTypes.Role
        // and Koan.permission survive intact for authorization.
        options.MapInboundClaims = false;

        // Accept credentials from every trust-fabric issuer: the default (HS256 service-mesh) plus every
        // asymmetric (ES256) tier — the embedded Authorization Server's user/agent tokens (SEC-0006 D1/D3).
        // The scheme asserts AUTHENTICITY only (signature + issuer + algorithm + lifetime). The per-resource
        // audience (RFC 8707 / SEC-0006 D2) is enforced by the resource server (e.g. the MCP edge), NOT here:
        // a single fixed audience at the scheme is precisely the confused-deputy bug being removed.
        var allIssuers = new List<IIssuer> { _issuer };
        allIssuers.AddRange(_asymmetricIssuers);

        var algorithms = allIssuers.Select(i => i.Algorithm).Distinct(StringComparer.Ordinal).ToArray();
        var validIssuers = allIssuers.Select(i => i.Issuer).Distinct(StringComparer.Ordinal).ToArray();
        var issuer = _issuer;
        var asymmetricIssuers = _asymmetricIssuers;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _issuer.SignatureKey, // singular retained for the default tier
            // Resolve signing keys FRESH per validation (not a startup snapshot) so a rotated asymmetric key
            // (IIssuerKeyStore rotation) is picked up without a restart, and we use each issuer's real key
            // objects (the asymmetric issuer's active + retiring ECDsaSecurityKeys) rather than reconstructing
            // them from published JWKs. The JWKS form (IAsymmetricIssuer.PublishedKeys) is for publication only.
            IssuerSigningKeyResolver = (_, _, _, _) =>
            {
                var keys = new List<SecurityKey> { issuer.SignatureKey };
                foreach (var asym in asymmetricIssuers)
                {
                    var p = asym.CreateValidationParameters();
                    if (p.IssuerSigningKeys is not null) keys.AddRange(p.IssuerSigningKeys);
                    if (p.IssuerSigningKey is not null) keys.Add(p.IssuerSigningKey);
                }
                return keys;
            },
            ValidAlgorithms = algorithms, // SEC-0001 §6.1: pin the suite — alg=none / HS↔ES confusion unrepresentable
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = false, // SEC-0006 D2: per-resource audience enforced at the resource server
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
