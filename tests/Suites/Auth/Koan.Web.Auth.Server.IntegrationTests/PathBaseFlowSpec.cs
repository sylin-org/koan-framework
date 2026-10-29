using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — under a base URL with a path (<c>https://www.example.com/tangent</c>) the code flow runs end to end:
/// authorize redirects to the consent page under the path and sets the browser-binding cookie under the path, the
/// approval under the path carries that cookie and returns a code, and the PKCE exchange under the path issues a token
/// for the resource. The Device flow lands its verification page and its done page under the path.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class PathBaseFlowSpec : IClassFixture<BaseUrlPathFixture>
{
    private const string Base = BaseUrlPathFixture.PathBase;
    private const string Redirect = "http://127.0.0.1:7841/cb";
    private readonly BaseUrlPathFixture _fx;

    public PathBaseFlowSpec(BaseUrlPathFixture fx) => _fx = fx;

    private HttpClient Browser(out CookieContainer jar)
    {
        jar = new CookieContainer();
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = jar })
        { BaseAddress = new Uri(_fx.ListenUrl) };
    }

    [Fact]
    public async Task The_code_flow_runs_end_to_end_under_the_path()
    {
        await RegisterCodeClient(_fx.Services, "path-code", Redirect);
        var (verifier, challenge) = Pkce();
        using var browser = Browser(out var jar);
        (await browser.GetAsync($"{Base}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var authorize = await browser.GetAsync(Authorize(Base, "path-code", Redirect, challenge, BaseUrlPathFixture.ApplicationUrl, state: "st"), Ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var consent = authorize.Headers.Location!.ToString();
        consent.Should().StartWith($"{Base}/me/connect?rid=", "the consent page is the application's, under its path");
        var rid = Query(consent, "rid");
        var binding = authorize.Headers.GetValues("Set-Cookie").Should().ContainSingle(cookie => cookie.StartsWith("koan.oauth." + rid + "=", StringComparison.Ordinal)).Subject;
        binding.Split(';').Select(part => part.Trim()).Should().Contain($"path={Base}/oauth");
        jar.GetCookies(new Uri($"{_fx.ListenUrl}{Base}/oauth/request/{rid}/approve")).Should().Contain(cookie => cookie.Name == "koan.oauth." + rid);

        using (var seam = await browser.GetAsync($"{Base}/oauth/request/{rid}", Ct))
        {
            seam.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await seam.Content.ReadAsStringAsync(Ct));
            doc.RootElement.GetProperty("user").GetProperty("loggedIn").GetBoolean().Should().BeTrue();
        }

        using var approved = await Decide(browser, Base, rid, "approve");
        approved.StatusCode.Should().Be(HttpStatusCode.Redirect, await approved.Content.ReadAsStringAsync(Ct));
        var callback = approved.Headers.Location!.ToString();
        callback.Should().StartWith(Redirect);
        Query(callback, "state").Should().Be("st");

        var token = await Redeem(browser, Base, "path-code", Query(callback, "code"), verifier, Redirect);
        var jwt = Read(token);
        jwt.Audiences.Should().Contain(BaseUrlPathFixture.ApplicationUrl);
        jwt.Subject.Should().Be("alice");
        jwt.Issuer.Should().Be(BaseUrlPathFixture.ApplicationUrl);
    }

    [Fact]
    public async Task The_device_flow_lands_its_pages_under_the_path()
    {
        await RegisterDeviceClient(_fx.Services, "path-device");
        using var device = _fx.NewClient();
        using var started = await device.PostAsync($"{Base}/oauth/device", new FormUrlEncodedContent(
            [new("client_id", "path-device"), new("resource", BaseUrlPathFixture.ApplicationUrl)]), Ct);
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("verification_uri").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/me/connect");
        var userCode = doc.RootElement.GetProperty("user_code").GetString()!;
        var deviceCode = doc.RootElement.GetProperty("device_code").GetString()!;

        using var browser = Browser(out _);
        (await browser.GetAsync($"{Base}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var approved = await Decide(browser, Base, userCode, "approve");
        approved.StatusCode.Should().Be(HttpStatusCode.Redirect);
        approved.Headers.Location!.ToString().Should().StartWith($"{BaseUrlPathFixture.ApplicationUrl}/me/connect/done");

        var token = await PollForToken(device, Base, "path-device", deviceCode);
        Read(token).Audiences.Should().Contain(BaseUrlPathFixture.ApplicationUrl);
    }
}
