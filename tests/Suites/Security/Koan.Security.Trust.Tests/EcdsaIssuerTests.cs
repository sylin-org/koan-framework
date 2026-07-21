using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0006 D1/D2 — the asymmetric (ES256) issuer: signs with a P-256 key it never publishes the private half
/// of, round-trips claims, binds a per-call resource audience (RFC 8707), and validates authenticity while
/// leaving the per-resource audience check to the resource server (the confused-deputy split).
/// </summary>
public sealed class EcdsaIssuerTests
{
    private static EcdsaIssuer NewIssuer(IIssuerKeyStore? store = null) =>
        new(store ?? new EphemeralIssuerKeyStore(),
            Options.Create(new TrustIssuerOptions()),
            NullLogger<EcdsaIssuer>.Instance);

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
    public void Issue_binds_the_requested_resource_audience()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims { Subject = "alice" }, audience: "koan://mcp/resource");

        var aud = new JwtSecurityTokenHandler().ReadJwtToken(token).Audiences.ToArray();
        aud.Should().ContainSingle().Which.Should().Be("koan://mcp/resource");
    }

    [Fact]
    public void Issue_without_audience_falls_back_to_the_configured_default()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims { Subject = "alice" });

        new JwtSecurityTokenHandler().ReadJwtToken(token).Audiences.Should().ContainSingle().Which.Should().Be("koan");
    }

    [Fact]
    public void Published_keys_are_public_only_ES256_jwks()
    {
        var issuer = NewIssuer();

        issuer.PublishedKeys.Should().NotBeEmpty();
        var jwk = issuer.PublishedKeys[0];
        jwk.Kty.Should().Be("EC");
        jwk.Crv.Should().Be("P-256");
        jwk.Alg.Should().Be("ES256");
        jwk.Use.Should().Be("sig");
        jwk.Kid.Should().NotBeNullOrEmpty();
        jwk.X.Should().NotBeNullOrEmpty();
        jwk.Y.Should().NotBeNullOrEmpty();
        // The private component must never appear in a published JWK.
        jwk.D.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Validation_rejects_a_token_from_a_different_keypair()
    {
        var good = NewIssuer();
        var foreign = NewIssuer(); // a different ephemeral keypair

        var token = foreign.Issue(new TrustClaims { Subject = "mallory" });

        good.TryValidate(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Validation_is_audience_agnostic_aud_is_enforced_at_the_resource()
    {
        // SEC-0006 D2: the issuer asserts authenticity only; a token bound to one resource still validates
        // here (the resource server is responsible for aud == its own id). This documents the split.
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims { Subject = "alice" }, audience: "koan://some/other/resource");

        issuer.TryValidate(token, out _).Should().BeTrue();
    }

    [Fact]
    public void Issue_emits_granted_scopes_as_a_space_delimited_scope_claim()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(new TrustClaims { Subject = "alice", Scopes = new[] { "orders:read", "orders:fulfill" } });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        // RFC 9068 §2.2.3 — a single space-delimited `scope` claim (the authorization grant the gates read).
        var scope = jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value).Single();
        scope.Split(' ').Should().BeEquivalentTo(new[] { "orders:read", "orders:fulfill" });
        // Scopes are NOT folded into Koan.permission (SEC-0001 §8: permissions carry no authorization effect).
        jwt.Claims.Any(c => c.Type == "Koan.permission").Should().BeFalse();
    }

    [Fact]
    public void A_foreign_HS256_token_is_rejected_alg_is_pinned()
    {
        // alg-pinning: an HS256 token must never validate against the ES256 issuer (HS↔ES confusion).
        var es = NewIssuer();
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var hsToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "koan-dev",
            audience: "koan",
            claims: [new Claim("sub", "x")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        es.TryValidate(hsToken, out _).Should().BeFalse();
    }
}
