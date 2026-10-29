using System.Net;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0028 — a user-code lookup through an application's page counts against the same per-address allowance as Koan's
/// own <c>/oauth/request/{code}</c>: with two attempts a minute, two lookups through the page spend it, the third is
/// throttled, and Koan's endpoint then answers 429 too.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class ConsentPageAllowanceSpec : IClassFixture<ThrottledConsentFixture>
{
    private const string Base = ConsentPageFixture.Base;
    private readonly ThrottledConsentFixture _fx;

    public ConsentPageAllowanceSpec(ThrottledConsentFixture fx) => _fx = fx;

    [Fact]
    public async Task The_page_and_Koans_endpoint_share_one_allowance()
    {
        using var browser = _fx.NewClient();
        (await browser.GetAsync($"{Base}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await browser.GetAsync($"{Base}/consent?user_code=bcdf-ghjk", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await browser.GetAsync($"{Base}/consent?user_code=bcdf-ghjm", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await browser.GetAsync($"{Base}/consent?user_code=bcdf-ghjn", Ct)).StatusCode.Should().Be((HttpStatusCode)429);
        (await browser.GetAsync($"{Base}/oauth/request/BCDFGHJP", Ct)).StatusCode.Should().Be((HttpStatusCode)429,
            "the page spent the allowance Koan's own request endpoint draws on");
    }
}
