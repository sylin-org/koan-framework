using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 addendum (WEB-0072 P3) — the Development dev-client auto-seed. The MCP Explorer's device-flow
/// exerciser needs a pre-registered public client to play RFC 8628, and DCR is loopback-only (a same-origin
/// browser can't self-register). The AS seeds <c>koan-dev-explorer</c> on boot in Development; this proves it
/// exists, is usable, and lets the device grant run <b>without</b> the manual <c>RegisterClientAsync</c> the other
/// device specs need — that removal is the whole point of the seed.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class DevExplorerClientSeedSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public DevExplorerClientSeedSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";

    [Fact]
    public async Task Seeded_dev_client_is_present_active_and_public()
    {
        var client = await OAuthClient.Get(AuthServerModule.DevExplorerClientId, Ct);

        client.Should().NotBeNull("the AS seeds it on a Development boot");
        client!.IsPublic.Should().BeTrue("a public client — no secret; PKCE + device consent are the protection");
        client.IsDynamic.Should().BeFalse("not a DCR client — not loopback-constrained, not GC-swept");
        client.ExpiresUtc.Should().BeNull("no expiry — it is re-seeded idempotently each dev boot");
        client.IsActive(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task Polling_the_seeded_client_before_approval_is_pending()
    {
        using var device = _fx.NewClient();
        var (deviceCode, _) = await RequestDeviceAsync(device);

        var res = await PollAsync(device, deviceCode);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("authorization_pending");
    }

    [Fact]
    public async Task Device_flow_issues_a_token_after_approval_without_manual_registration()
    {
        // NOTE: no RegisterClientAsync here — the boot seed is what makes `koan-dev-explorer` resolvable.
        using var device = _fx.NewClient();
        var (deviceCode, userCode) = await RequestDeviceAsync(device);

        await UserApprovesAsync(userCode);

        // First poll after approval (no prior poll → no slow_down) → the token, exactly as the exerciser drives it.
        var granted = await PollAsync(device, deviceCode);
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await granted.Content.ReadAsStringAsync(Ct));
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Audiences.Should().Contain(Resource); // RFC 8707 — aud bound to the requested MCP resource
        jwt.Subject.Should().Be("alice");          // the user who approved in their browser session
    }

    private async Task<(string deviceCode, string userCode)> RequestDeviceAsync(HttpClient device)
    {
        var res = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AuthServerModule.DevExplorerClientId,
            ["scope"] = "mcp.read",
            ["resource"] = Resource,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return (doc.RootElement.GetProperty("device_code").GetString()!, doc.RootElement.GetProperty("user_code").GetString()!);
    }

    private Task<HttpResponseMessage> PollAsync(HttpClient device, string deviceCode)
        => device.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = deviceCode,
            ["client_id"] = AuthServerModule.DevExplorerClientId,
        }), Ct);

    private async Task UserApprovesAsync(string userCode)
    {
        using var user = _fx.NewClient();
        (await user.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync($"/oauth/request/{userCode}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{userCode}/approve");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        (await user.SendAsync(req, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
