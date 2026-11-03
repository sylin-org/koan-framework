using System.Security.Claims;
using AwesomeAssertions;
using Koan.Security.Trust.Inbound;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0025 — the resource rule both edges share: a token's audience names the resource with or without one trailing
/// slash, compared ordinally, and an unbound token names none; the challenge carries only what it is given.
/// </summary>
public sealed class KoanBearerResourceTests
{
    private static ClaimsPrincipal With(params string[] audiences)
        => new(new ClaimsIdentity(audiences.Select(audience => new Claim("aud", audience)), "test"));

    [Theory]
    [InlineData("https://example.test/notes", "https://example.test/notes", true)]
    [InlineData("https://example.test/notes/", "https://example.test/notes", true)]
    [InlineData("https://example.test/notes", "https://example.test/notes/", true)]
    [InlineData("https://example.test/notes//", "https://example.test/notes", false)]
    [InlineData("https://example.test/notes/mcp", "https://example.test/notes", false)]
    [InlineData("https://EXAMPLE.test/notes", "https://example.test/notes", false)]
    [InlineData("urn:example:notes", "urn:example:notes", true)]
    public void The_audience_names_the_resource_with_or_without_a_trailing_slash(string audience, string resource, bool accepted)
        => KoanBearerResource.Accepts(With(audience), resource).Should().Be(accepted);

    [Fact]
    public void A_token_without_an_audience_names_no_resource()
        => KoanBearerResource.Accepts(With(), "https://example.test").Should().BeFalse();

    [Fact]
    public void The_challenge_carries_only_what_it_is_given()
    {
        KoanBearerResource.Challenge().Should().Be("Bearer");
        KoanBearerResource.Challenge(KoanBearerResource.InvalidToken).Should().Be("Bearer error=\"invalid_token\"");
    }
}
