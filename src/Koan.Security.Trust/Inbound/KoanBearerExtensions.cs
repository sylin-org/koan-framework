using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust.Inbound;

/// <summary>
/// Registers Trust's inbound bearer scheme with an algorithm-pinned issuer contract. It is additive and
/// non-default: cookie may remain the default scheme while endpoints deliberately opt into bearer.
/// </summary>
internal static class KoanBearerExtensions
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

    public ConfigureKoanBearerOptions(IIssuer issuer) => _issuer = issuer;

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, KoanBearerDefaults.AuthenticationScheme, StringComparison.Ordinal)) return;
        // Keep claim types exactly as the issuer wrote them (no short-name remap), so ClaimTypes.Role
        // and Koan.permission survive intact for authorization.
        options.MapInboundClaims = false;

        // The scheme asserts authenticity only (signature + issuer + algorithm + lifetime). A resource-specific
        // audience is enforced by the resource server because a single global audience would recreate the
        // confused-deputy bug.
        var issuer = _issuer;
        var parameters = issuer.CreateValidationParameters();
        // Resolve the ring afresh per validation so persisted rotation is visible without restarting the host.
        parameters.IssuerSigningKeyResolver = (_, _, _, _) =>
        {
            var current = issuer.CreateValidationParameters();
            if (current.IssuerSigningKeys is not null) return current.IssuerSigningKeys;
            return current.IssuerSigningKey is null ? [] : [current.IssuerSigningKey];
        };
        options.TokenValidationParameters = parameters;
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
