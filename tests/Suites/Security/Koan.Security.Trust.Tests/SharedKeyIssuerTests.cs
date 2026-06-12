using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0003 — the shared-secret issuer: HS256, self-mint round-trip, and the defining property that any two
/// issuers sharing the same secret validate each other's tokens (cross-service self-mint), while a different
/// secret does not. Plus: the default key is the well-known insecure value.
/// </summary>
public sealed class SharedKeyIssuerTests
{
    private static SharedKeyIssuer NewIssuer(string? key = null) =>
        new(Options.Create(key is null ? new TrustIssuerOptions() : new TrustIssuerOptions { Key = key }),
            NullLogger<SharedKeyIssuer>.Instance);

    [Fact]
    public void Issued_token_is_HS256_and_round_trips_with_claims()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims
        {
            Subject = "alice",
            Email = "alice@local",
            Roles = new[] { "admin" },
            Permissions = new[] { "recs:write" },
        });

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Alg.Should().Be("HS256");

        issuer.TryValidate(token, out var principal).Should().BeTrue();
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("admin");
        principal.FindAll("Koan.permission").Select(c => c.Value).Should().Contain("recs:write");
    }

    [Fact]
    public void Same_secret_validates_across_issuers_cross_service_self_mint()
    {
        // Two independent issuers (= two services) holding the SAME secret trust each other's tokens.
        var a = NewIssuer("team-secret");
        var b = NewIssuer("team-secret");
        a.KeyId.Should().Be(b.KeyId);

        var token = a.Issue(new TrustClaims { Subject = "svc-a", Roles = new[] { "service" } });

        b.TryValidate(token, out var principal).Should().BeTrue();
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("service");
    }

    [Fact]
    public void Different_secret_does_not_validate()
    {
        var a = NewIssuer("secret-A");
        var b = NewIssuer("secret-B");

        var token = a.Issue(new TrustClaims { Subject = "x" });

        b.TryValidate(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Default_key_is_the_well_known_insecure_value()
    {
        new TrustIssuerOptions().Key.Should().Be("super-insecure-shared-secret-replace-asap");
        TrustIssuerOptions.DefaultInsecureKey.Should().Be(new TrustIssuerOptions().Key);
    }
}
