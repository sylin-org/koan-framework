using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0026 — with Web Auth's resource edge composed, so the application's default scheme authenticates a request by
/// its bearer, the authorization server still decides only on the person's session: a bearer for a person who approved
/// a client before does not take the remembered-consent path at <c>/oauth/authorize</c> (no code, no browser), cannot
/// approve a device request, and reads as not signed in at the consent seam. The same person's browser session still
/// takes the remembered path.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class SessionDecidesSpec : IClassFixture<ResourceEdgeFlowFixture>
{
    private const string Redirect = "http://127.0.0.1:7831/cb";
    private readonly ResourceEdgeFlowFixture _fx;

    public SessionDecidesSpec(ResourceEdgeFlowFixture fx) => _fx = fx;

    /// <summary>The person approves <paramref name="clientId"/> once in their browser and the client redeems the code
    /// for a token issued for the application's pages; the browser and the token are returned.</summary>
    private async Task<(HttpClient Browser, string Token)> ApproveOnce(string clientId)
    {
        await RegisterCodeClient(_fx.Services, clientId, Redirect);
        var (verifier, challenge) = Pkce();
        var browser = _fx.NewClient();
        (await browser.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var authorize = await browser.GetAsync(Authorize("", clientId, Redirect, challenge, _fx.BaseUrl), Ct);
        using var approved = await Decide(browser, "", Query(authorize.Headers.Location!.ToString(), "rid"), "approve");
        var token = await Redeem(browser, "", clientId, Query(approved.Headers.Location!.ToString(), "code"), verifier, Redirect);
        return (browser, token);
    }

    private static HttpRequestMessage AsAgent(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task A_bearer_for_the_person_does_not_take_the_remembered_consent_path()
    {
        var (browser, token) = await ApproveOnce("remembered-client");
        using var _ = browser;
        using var agent = _fx.NewClient();

        using var asAgent = await agent.SendAsync(AsAgent(HttpMethod.Get, Authorize("", "remembered-client", Redirect, Pkce().Challenge, _fx.BaseUrl), token), Ct);

        asAgent.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = asAgent.Headers.Location!.ToString();
        location.Should().StartWith("/me/connect?rid=", "a bearer is not a browser session, so the person is asked");
        Query(location, "code").Should().BeEmpty();

        using var asPerson = await browser.GetAsync(Authorize("", "remembered-client", Redirect, Pkce().Challenge, _fx.BaseUrl), Ct);
        asPerson.Headers.Location!.ToString().Should().StartWith(Redirect, "the person's own session still takes the remembered path");
        Query(asPerson.Headers.Location!.ToString(), "code").Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_bearer_for_the_person_cannot_approve_a_device_request()
    {
        var (browser, token) = await ApproveOnce("device-approver");
        using var _ = browser;
        await RegisterDeviceClient(_fx.Services, "device-target");
        using var device = _fx.NewClient();
        var (deviceCode, userCode) = await StartDevice(device, "", "device-target", _fx.BaseUrl);

        using var agent = _fx.NewClient();
        using var approve = await agent.SendAsync(AsAgent(HttpMethod.Post, $"/oauth/request/{userCode}/approve", token), Ct);

        approve.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await approve.Content.ReadAsStringAsync(Ct)).Should().Contain("login_required");
        using var poll = await device.PostAsync("/oauth/token", new FormUrlEncodedContent(new System.Collections.Generic.Dictionary<string, string>
        {
            ["grant_type"] = Protocol.OAuthGrantTypes.DeviceCode,
            ["device_code"] = deviceCode,
            ["client_id"] = "device-target",
        }), Ct);
        (await poll.Content.ReadAsStringAsync(Ct)).Should().Contain("authorization_pending");
    }

    [Fact]
    public async Task A_bearer_for_the_person_reads_as_not_signed_in_at_the_consent_seam()
    {
        var (browser, token) = await ApproveOnce("seam-client");
        using var _ = browser;
        await RegisterCodeClient(_fx.Services, "seam-target", Redirect);
        using var authorize = await browser.GetAsync(Authorize("", "seam-target", Redirect, Pkce().Challenge, _fx.BaseUrl), Ct);
        var rid = Query(authorize.Headers.Location!.ToString(), "rid");

        using var agent = _fx.NewClient();
        using var seam = await agent.SendAsync(AsAgent(HttpMethod.Get, $"/oauth/request/{rid}", token), Ct);

        seam.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await seam.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("user").GetProperty("loggedIn").GetBoolean().Should().BeFalse();
    }
}
