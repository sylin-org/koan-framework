using System.IdentityModel.Tokens.Jwt;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// SEC-0001 §11: the dev TestProvider must sign with a per-process, non-deterministic asymmetric
/// (ES256) key — closing the prior footgun where the symmetric key was derivable from a public formula.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private static TestProviderOptions Options() => new()
    {
        UseJwtTokens = true,
        JwtIssuer = "koan-test-provider",
        JwtAudience = "koan-test-client",
        JwtExpirationMinutes = 60,
    };

    private static JwtTokenService NewService() => new(NullLogger<JwtTokenService>.Instance);

    [Fact]
    public void Token_is_ES256_signed_and_round_trips_with_claims()
    {
        var svc = NewService();
        var opts = Options();
        var env = new DevTokenStore.ClaimEnvelope();
        env.Roles.Add("admin");
        env.Permissions.Add("recs:write");

        var token = svc.CreateToken(new UserProfile("alice", "alice@local", null), env, opts);

        // Asymmetric ES256 in the header — never the old symmetric HS256.
        var header = new JwtSecurityTokenHandler().ReadJwtToken(token).Header;
        header.Alg.Should().Be("ES256");

        svc.ValidateToken(token, opts, out var profile, out var outEnv).Should().BeTrue();
        profile.Email.Should().Be("alice@local");
        outEnv.Roles.Should().Contain("admin");
        outEnv.Permissions.Should().Contain("recs:write");
    }

    [Fact]
    public void Signing_key_is_non_deterministic_per_process()
    {
        // Two independent services get different random keypairs (distinct kid),
        // and a token minted by one cannot be validated by the other — i.e. a verifier
        // cannot forge, and the key is not reconstructible from config.
        var a = NewService();
        var b = NewService();
        var opts = Options();

        a.KeyId.Should().NotBe(b.KeyId);

        var token = a.CreateToken(new UserProfile("x", "x@local", null), new DevTokenStore.ClaimEnvelope(), opts);

        a.ValidateToken(token, opts, out _, out _).Should().BeTrue();
        b.ValidateToken(token, opts, out _, out _).Should().BeFalse();
    }
}
