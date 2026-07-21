using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 4 — the Device Authorization Grant (RFC 8628, D8) end-to-end: device request → poll
/// (authorization_pending) → the user approves on a second device via the unified consent seam → poll → token.
/// Plus the negatives: unknown device_code, denial, and slow_down.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthDeviceFlowSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public OAuthDeviceFlowSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";

    private static async Task RegisterClientAsync(string clientId)
    {
        var client = new OAuthClient { Id = clientId, ClientName = "Headless CLI", CreatedUtc = DateTimeOffset.UtcNow };
        await client.Save(Ct);
    }

    private async Task<(string deviceCode, string userCode)> RequestDeviceAsync(HttpClient device, string clientId)
    {
        var res = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "mcp.read",
            ["resource"] = Resource,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("verification_uri").GetString().Should().Be($"{_fx.BaseUrl}/me/connect");
        doc.RootElement.GetProperty("interval").GetInt32().Should().BeGreaterThan(0);
        return (doc.RootElement.GetProperty("device_code").GetString()!, doc.RootElement.GetProperty("user_code").GetString()!);
    }

    private Task<HttpResponseMessage> PollAsync(HttpClient device, string clientId, string deviceCode)
        => device.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = deviceCode,
            ["client_id"] = clientId,
        }), Ct);

    private async Task UserApprovesAsync(string userCode, string verb = "approve")
    {
        using var user = _fx.NewClient();
        (await user.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        // the user's browser shows the typed code in context, then approves
        (await user.GetAsync($"/oauth/request/{userCode}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{userCode}/{verb}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        (await user.SendAsync(req, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Full_device_flow_issues_a_token_after_approval()
    {
        await RegisterClientAsync("device-happy");
        using var device = _fx.NewClient();
        var (deviceCode, userCode) = await RequestDeviceAsync(device, "device-happy");

        await UserApprovesAsync(userCode);

        // First poll after approval (no prior poll → no slow_down) → the token.
        var granted = await PollAsync(device, "device-happy", deviceCode);
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await granted.Content.ReadAsStringAsync(Ct));
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Audiences.Should().Contain(Resource);
        jwt.Subject.Should().Be("alice");

        // Single-use: a second redemption of the device_code fails (the Consumed check fires before slow_down).
        var replay = await PollAsync(device, "device-happy", deviceCode);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await replay.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Polling_before_approval_returns_authorization_pending()
    {
        await RegisterClientAsync("device-pending");
        using var device = _fx.NewClient();
        var (deviceCode, _) = await RequestDeviceAsync(device, "device-pending");

        var res = await PollAsync(device, "device-pending", deviceCode);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("authorization_pending");
    }

    [Fact]
    public async Task An_unknown_device_code_is_rejected()
    {
        await RegisterClientAsync("device-unknown");
        using var device = _fx.NewClient();
        var res = await PollAsync(device, "device-unknown", "not-a-real-device-code");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task A_denied_device_request_returns_access_denied()
    {
        await RegisterClientAsync("device-deny");
        using var device = _fx.NewClient();
        var (deviceCode, userCode) = await RequestDeviceAsync(device, "device-deny");

        await UserApprovesAsync(userCode, verb: "deny");

        var res = await PollAsync(device, "device-deny", deviceCode);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("access_denied");
    }

    [Fact]
    public async Task Polling_too_fast_is_throttled_with_slow_down()
    {
        await RegisterClientAsync("device-slow");
        using var device = _fx.NewClient();
        var (deviceCode, _) = await RequestDeviceAsync(device, "device-slow");

        (await PollAsync(device, "device-slow", deviceCode)).StatusCode.Should().Be(HttpStatusCode.BadRequest); // pending, stamps last-poll
        var second = await PollAsync(device, "device-slow", deviceCode); // immediately again, within the interval
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync(Ct)).Should().Contain("slow_down");
    }
}
