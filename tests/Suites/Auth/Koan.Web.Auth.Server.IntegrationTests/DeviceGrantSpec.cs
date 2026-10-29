using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0024 — the Device Authorization Grant is a client's explicit permission. A dynamically registered client is
/// allowed the code grant only, so it is refused at <c>/oauth/device</c>; a pre-registered client that names no grant
/// is refused there too; one that names the device grant is accepted. A client's named grants bind the code grant as
/// well: a client allowed only the device grant is refused at <c>/oauth/authorize</c>. The permission is read at every step
/// of a device request, so one whose client loses the grant after it was made is not shown, approved or exchanged.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class DeviceGrantSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public DeviceGrantSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";
    private const string Redirect = "http://127.0.0.1:7811/cb";

    private Task<HttpResponseMessage> StartDevice(HttpClient http, string clientId)
        => http.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "mcp.read",
            ["resource"] = Resource,
        }), Ct);

    private static async Task<string> Error(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task A_dynamically_registered_client_is_refused_at_the_device_endpoint()
    {
        using var http = _fx.NewClient();
        using var registered = await http.PostAsync("/oauth/register", new StringContent(
            "{\"client_name\":\"Lobby\",\"redirect_uris\":[\"" + Redirect + "\"],\"grant_types\":[\"urn:ietf:params:oauth:grant-type:device_code\"]}",
            Encoding.UTF8, "application/json"), Ct);
        registered.StatusCode.Should().Be(HttpStatusCode.Created);
        using var registration = JsonDocument.Parse(await registered.Content.ReadAsStringAsync(Ct));
        registration.RootElement.GetProperty("grant_types").EnumerateArray().Should()
            .ContainSingle().Which.GetString().Should().Be(OAuthGrantTypes.AuthorizationCode,
                "dynamic registration never grants the device flow, whatever the request asks");
        var clientId = registration.RootElement.GetProperty("client_id").GetString()!;

        using var res = await StartDevice(http, clientId);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(res)).Should().Be("unauthorized_client");
    }

    [Fact]
    public async Task A_pre_registered_client_that_names_no_grant_is_refused_at_the_device_endpoint()
    {
        await new OAuthClient { Id = "device-unnamed", ClientName = "Unnamed", CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);
        using var http = _fx.NewClient();

        using var res = await StartDevice(http, "device-unnamed");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(res)).Should().Be("unauthorized_client");
    }

    [Fact]
    public async Task A_client_allowed_the_device_grant_is_accepted()
    {
        await new OAuthClient
        {
            Id = "device-allowed",
            ClientName = "Allowed",
            GrantTypes = [OAuthGrantTypes.DeviceCode],
            CreatedUtc = DateTimeOffset.UtcNow,
        }.Save(Ct);
        using var http = _fx.NewClient();

        using var res = await StartDevice(http, "device-allowed");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("device_code").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_client_allowed_only_the_device_grant_is_refused_at_authorize()
    {
        await new OAuthClient
        {
            Id = "device-only",
            ClientName = "Device only",
            RedirectUris = [Redirect],
            GrantTypes = [OAuthGrantTypes.DeviceCode],
            CreatedUtc = DateTimeOffset.UtcNow,
        }.Save(Ct);
        var challenge = OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(OpaqueToken.New(32))));
        using var http = _fx.NewClient();

        using var res = await http.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "device-only",
            ["redirect_uri"] = Redirect,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = Resource,
            ["state"] = "s",
        }), Ct);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = res.Headers.Location!.ToString();
        location.Should().StartWith(Redirect);
        QueryHelpers.ParseQuery(new Uri(location).Query)["error"].ToString().Should().Be("unauthorized_client");
    }

    [Fact]
    public async Task A_pending_request_whose_client_lost_the_device_grant_is_not_shown_approved_or_exchanged()
    {
        var (deviceCode, userCode) = await StartAllowedDevice("device-revoked-pending");
        await NameGrants("device-revoked-pending");
        using var browser = await SignedIn();

        using (var page = await browser.GetAsync($"/consent?user_code={userCode}", Ct))
            page.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ConsentPage.Find reports the client not allowed, and the page refuses it");
        using (var seam = await browser.GetAsync($"/oauth/request/{userCode}", Ct))
        {
            seam.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Error(seam)).Should().Be("unauthorized_client");
        }
        using (var approve = await Approve(browser, userCode))
        {
            approve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Error(approve)).Should().Be("unauthorized_client");
        }
        (await DeviceCode.Get(deviceCode, Ct))!.Status.Should().Be(DeviceCode.StatusPending, "a refused approval decides nothing");

        using var poll = await Poll(browser, "device-revoked-pending", deviceCode);
        poll.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(poll)).Should().Be("unauthorized_client");
    }

    [Fact]
    public async Task A_request_approved_before_its_client_lost_the_device_grant_is_not_exchanged()
    {
        var (deviceCode, userCode) = await StartAllowedDevice("device-revoked-approved");
        using var browser = await SignedIn();
        using (var page = await browser.GetAsync($"/consent?user_code={userCode}", Ct))
            page.StatusCode.Should().Be(HttpStatusCode.OK, "a client with the grant is found as before");
        using (var approve = await Approve(browser, userCode))
            approve.StatusCode.Should().Be(HttpStatusCode.OK, "a client with the grant is approved as before");

        await NameGrants("device-revoked-approved", OAuthGrantTypes.AuthorizationCode);

        using var poll = await Poll(browser, "device-revoked-approved", deviceCode);
        poll.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Error(poll)).Should().Be("unauthorized_client");
    }

    private async Task<(string DeviceCode, string UserCode)> StartAllowedDevice(string clientId)
    {
        await new OAuthClient
        {
            Id = clientId,
            ClientName = "Revoked later",
            GrantTypes = [OAuthGrantTypes.DeviceCode],
            CreatedUtc = DateTimeOffset.UtcNow,
        }.Save(Ct);
        using var device = _fx.NewClient();
        using var res = await StartDevice(device, clientId);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return (doc.RootElement.GetProperty("device_code").GetString()!, doc.RootElement.GetProperty("user_code").GetString()!);
    }

    /// <summary>An operator changes the client's grants after its device request was made.</summary>
    private static async Task NameGrants(string clientId, params string[] grants)
    {
        var client = (await OAuthClient.Get(clientId, Ct))!;
        client.GrantTypes = [.. grants];
        await client.Save(Ct);
    }

    private async Task<HttpClient> SignedIn()
    {
        var browser = _fx.NewClient();
        (await browser.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return browser;
    }

    private static Task<HttpResponseMessage> Approve(HttpClient browser, string userCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{userCode}/approve");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return browser.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> Poll(HttpClient device, string clientId, string deviceCode)
        => device.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.DeviceCode,
            ["device_code"] = deviceCode,
            ["client_id"] = clientId,
        }), Ct);
}
