using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Auth.Server.Protocol;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0025 — with Web Auth's resource edge on, a client that attaches its expired token by default still talks to the
/// authorization server: a refresh grant returns new tokens, the device and registration endpoints give their own
/// answers, and the discovery documents answer 200, because the server's protocol endpoints never authenticate by
/// bearer. An application action that admits anonymous callers still answers 401 <c>Bearer error="invalid_token"</c>.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class ResourceEdgeExemptionSpec : IClassFixture<ResourceEdgeFlowFixture>
{
    private const string Redirect = "http://127.0.0.1:7861/cb";
    private readonly ResourceEdgeFlowFixture _fx;

    public ResourceEdgeExemptionSpec(ResourceEdgeFlowFixture fx) => _fx = fx;

    private string Expired() => Sign(_fx.Services, _fx.BaseUrl, _fx.BaseUrl, expired: true);

    private HttpRequestMessage WithStaleToken(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Expired());
        return request;
    }

    [Fact]
    public async Task A_refresh_grant_carrying_an_expired_token_returns_new_tokens()
    {
        await RegisterCodeClient(_fx.Services, "stale-refresher", Redirect);
        var (verifier, challenge) = Pkce();
        using var browser = _fx.NewClient();
        (await browser.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var authorize = await browser.GetAsync(Authorize("", "stale-refresher", Redirect, challenge, _fx.BaseUrl), Ct);
        using var approved = await Decide(browser, "", Query(authorize.Headers.Location!.ToString(), "rid"), "approve");
        var (_, refresh) = await RedeemBoth(browser, "", "stale-refresher", Query(approved.Headers.Location!.ToString(), "code"), verifier, Redirect);

        using var agent = _fx.NewClient();
        using var refreshed = await agent.SendAsync(WithStaleToken(HttpMethod.Post, "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.RefreshToken,
            ["refresh_token"] = refresh,
            ["client_id"] = "stale-refresher",
        })), Ct);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBe(refresh);
    }

    [Fact]
    public async Task The_device_registration_and_discovery_endpoints_answer_in_their_own_terms()
    {
        await RegisterDeviceClient(_fx.Services, "stale-device");
        using var agent = _fx.NewClient();

        using var device = await agent.SendAsync(WithStaleToken(HttpMethod.Post, "/oauth/device", new FormUrlEncodedContent(
            [new("client_id", "stale-device"), new("resource", _fx.BaseUrl)])), Ct);
        using var register = await agent.SendAsync(WithStaleToken(HttpMethod.Post, "/oauth/register", new StringContent(
            "{\"client_name\":\"Agent\",\"redirect_uris\":[\"" + Redirect + "\"]}", Encoding.UTF8, "application/json")), Ct);
        using var metadata = await agent.SendAsync(WithStaleToken(HttpMethod.Get, "/.well-known/oauth-authorization-server"), Ct);
        using var jwks = await agent.SendAsync(WithStaleToken(HttpMethod.Get, "/.well-known/jwks.json"), Ct);
        using var unknownClient = await agent.SendAsync(WithStaleToken(HttpMethod.Post, "/oauth/device", new FormUrlEncodedContent(
            [new("client_id", "nobody"), new("resource", _fx.BaseUrl)])), Ct);

        device.StatusCode.Should().Be(HttpStatusCode.OK);
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        metadata.StatusCode.Should().Be(HttpStatusCode.OK);
        jwks.StatusCode.Should().Be(HttpStatusCode.OK);
        unknownClient.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the RFC 6749 §5.2 error, not the edge's 401");
        (await unknownClient.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task An_application_action_still_answers_401_to_the_expired_token()
    {
        using var agent = _fx.NewClient();
        using var res = await agent.SendAsync(WithStaleToken(HttpMethod.Get, "/test/anonymous"), Ct);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }
}
