using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust.Inbound;

/// <summary>
/// SEC-0001 §6.1/§11 — registers the inbound bearer scheme that validates KSVIDs against the issuer's
/// public key with the algorithm pinned (ES256). Additive and non-default: cookie remains the default
/// scheme; bearer is opted into per endpoint. Called by <c>Koan.Web.Auth.AddKoanWebAuth()</c> alongside
/// the cookie scheme — Trust never references Web.Auth back up.
/// </summary>
public static class KoanBearerExtensions
{
    public static AuthenticationBuilder AddKoanBearer(this AuthenticationBuilder builder)
    {
        builder.AddJwtBearer(KoanBearerDefaults.AuthenticationScheme, _ => { });
        // The issuer is a singleton resolved at options-configuration time; its public key + iss/aud/alg
        // are the single validation contract (IIssuer.CreateValidationParameters) the scheme enforces.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, ConfigureKoanBearerOptions>());
        return builder;
    }
}

internal sealed class ConfigureKoanBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IIssuer _issuer;

    public ConfigureKoanBearerOptions(IIssuer issuer) => _issuer = issuer;

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, KoanBearerDefaults.AuthenticationScheme, StringComparison.Ordinal)) return;
        // Keep claim types exactly as the issuer wrote them (no short-name remap), so ClaimTypes.Role
        // and Koan.permission survive intact for authorization.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = _issuer.CreateValidationParameters();
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
