using System;
using System.IdentityModel.Tokens.Jwt;
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
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<TrustIssuerOptions>>(Options.Create(new TrustIssuerOptions()));
        services.AddSingleton<IIssuer, SharedKeyIssuer>();
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
}
