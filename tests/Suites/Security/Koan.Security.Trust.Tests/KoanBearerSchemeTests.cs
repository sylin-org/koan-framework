using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Koan.Security.Trust.Inbound;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0001 Phase 2 (2d): the inbound bearer scheme is wired to the issuer's alg-pinned, public-key
/// validation contract — it accepts KSVIDs from this issuer and rejects tokens signed by any other key.
/// (The end-to-end [Authorize] 200/401 behaviour is covered by the hosted ARCH-0079 spec in 2g.)
/// </summary>
public sealed class KoanBearerSchemeTests
{
    private static ServiceProvider BuildProvider(bool withAsymmetric = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<TrustIssuerOptions>>(Options.Create(new TrustIssuerOptions()));
        services.AddSingleton<IIssuer, SharedKeyIssuer>();
        if (withAsymmetric)
        {
            services.AddSingleton<IIssuerKeyStore, EphemeralIssuerKeyStore>();
            services.AddSingleton<IAsymmetricIssuer, EcdsaIssuer>();
        }
        services.AddAuthentication().AddKoanBearer();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Bearer_options_are_configured_from_the_issuer()
    {
        using var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(KoanBearerDefaults.AuthenticationScheme);

        opts.TokenValidationParameters.Should().NotBeNull();
        opts.TokenValidationParameters.IssuerSigningKey.Should().NotBeNull();
        opts.TokenValidationParameters.ValidAlgorithms.Should().Contain("HS256");
        opts.MapInboundClaims.Should().BeFalse();
    }

    [Fact]
    public void Scheme_accepts_issuer_tokens_and_rejects_foreign_keys()
    {
        using var sp = BuildProvider();
        var issuer = sp.GetRequiredService<IIssuer>();
        var tvp = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(KoanBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        var handler = new JwtSecurityTokenHandler();

        var good = issuer.Issue(new TrustClaims { Subject = "alice", Roles = new[] { "admin" } });
        handler.ValidateToken(good, tvp, out _); // does not throw

        // "Foreign" now means a DIFFERENT secret — two issuers sharing the same key validate each other by design.
        var foreign = new SharedKeyIssuer(Options.Create(new TrustIssuerOptions { Key = "a-different-secret" }), NullLogger<SharedKeyIssuer>.Instance)
            .Issue(new TrustClaims { Subject = "mallory" });
        Action validateForeign = () => handler.ValidateToken(foreign, tvp, out _);
        validateForeign.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void Scheme_accepts_asymmetric_ES256_tokens_when_the_asymmetric_tier_is_present()
    {
        // SEC-0006 D3 — once the asymmetric (ES256) issuer is registered (the Authorization Server tier), the
        // bearer scheme validates its tokens alongside the HS256 service-mesh tier.
        using var sp = BuildProvider(withAsymmetric: true);
        var asym = sp.GetRequiredService<IAsymmetricIssuer>();
        var tvp = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(KoanBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        tvp.ValidAlgorithms.Should().Contain("ES256");
        // A resource-audience-bound ES256 token validates here (authenticity only — aud is the edge's job).
        var token = asym.Issue(new TrustClaims { Subject = "alice", Roles = new[] { "user" } }, audience: "koan://mcp/resource");
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, tvp, out _);
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("user");

        // A foreign ES256 keypair is still rejected.
        var foreign = new EcdsaIssuer(new EphemeralIssuerKeyStore(), Options.Create(new TrustIssuerOptions()), NullLogger<EcdsaIssuer>.Instance)
            .Issue(new TrustClaims { Subject = "mallory" });
        Action validateForeign = () => new JwtSecurityTokenHandler().ValidateToken(foreign, tvp, out _);
        validateForeign.Should().Throw<SecurityTokenException>();
    }
}
