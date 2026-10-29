using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — one rule for the consent and done paths: they are application-relative and get the path base exactly once
/// in the authorize redirect, the device <c>verification_uri</c> and the done redirect alike.
/// </summary>
public abstract class ConsentPathRuleSpec<TFixture>(TFixture fx, string consent, string done) where TFixture : BasePathFixture
{
    private const string Redirect = "http://127.0.0.1:7881/cb";

    [Fact]
    public async Task The_redirect_the_verification_uri_and_the_done_redirect_agree()
    {
        var prefix = typeof(TFixture).Name;
        await RegisterCodeClient(fx.Services, prefix + "-code", Redirect);
        await RegisterDeviceClient(fx.Services, prefix + "-device");
        using var browser = fx.NewClient();
        (await browser.GetAsync($"{fx.PathBase}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var authorize = await browser.GetAsync(Authorize(fx.PathBase, prefix + "-code", Redirect, Pkce().Challenge, fx.ApplicationUrl), Ct);
        authorize.Headers.Location!.ToString().Should().StartWith(consent + "?rid=");

        using var device = fx.NewClient();
        using var started = await device.PostAsync($"{fx.PathBase}/oauth/device", new FormUrlEncodedContent(
            [new("client_id", prefix + "-device"), new("resource", fx.ApplicationUrl)]), Ct);
        using var doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("verification_uri").GetString().Should().Be("https://www.example.com" + consent);

        using var approved = await Decide(browser, fx.PathBase, doc.RootElement.GetProperty("user_code").GetString()!, "approve");
        approved.Headers.Location!.ToString().Should().StartWith("https://www.example.com" + done + "?outcome=");
    }
}

/// <summary>SEC-0027 — consent and done paths configured with the base already in them are still application-relative.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class PrefixedConsentPathRuleSpec(PrefixedConsentPathFixture fx)
    : ConsentPathRuleSpec<PrefixedConsentPathFixture>(fx, "/notes/notes/consent", "/notes/notes/consent/done"), IClassFixture<PrefixedConsentPathFixture>;

/// <summary>SEC-0027 — the default <c>/me/connect</c> under a base URL whose path is <c>/me</c>.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class MeBaseConsentPathRuleSpec(MeBasePathFixture fx)
    : ConsentPathRuleSpec<MeBasePathFixture>(fx, "/me/me/connect", "/me/me/connect/done"), IClassFixture<MeBasePathFixture>;
