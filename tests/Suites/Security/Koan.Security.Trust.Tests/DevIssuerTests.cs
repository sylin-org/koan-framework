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
/// SEC-0001 Phase 2 (2c): parity proof for the absorbed ES256 issuer. Mirrors the Test connector's
/// JwtTokenServiceTests against the new IIssuer seam, so both run green before the original is retired (2m).
/// </summary>
public sealed class DevIssuerTests
{
    private static DevIssuer NewIssuer() =>
        new(Options.Create(new TrustIssuerOptions()), NullLogger<DevIssuer>.Instance);

    [Fact]
    public void Issued_token_is_ES256_and_round_trips_with_claims()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims
        {
            Subject = "alice",
            Email = "alice@local",
            Roles = new[] { "admin" },
            Permissions = new[] { "recs:write" },
        });

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Alg.Should().Be("ES256");

        issuer.TryValidate(token, out var principal).Should().BeTrue();
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("admin");
        principal.FindAll("Koan.permission").Select(c => c.Value).Should().Contain("recs:write");
    }

    [Fact]
    public void Signing_key_is_non_deterministic_per_process()
    {
        var a = NewIssuer();
        var b = NewIssuer();
        a.KeyId.Should().NotBe(b.KeyId);

        var token = a.Issue(new TrustClaims { Subject = "x" });
        a.TryValidate(token, out _).Should().BeTrue();
        b.TryValidate(token, out _).Should().BeFalse();
    }
}
