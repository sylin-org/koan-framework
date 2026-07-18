using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
/// SEC-0006 — adversarial-review hardening regressions: cross-client code redemption, device client_id binding,
/// remembered-consent resource binding (audience-substitution fix), and state echo on error.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthHardeningSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public OAuthHardeningSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";
    private const string Redirect = "http://127.0.0.1:7799/cb";
    private const string Scope = "mcp.read";

    private static (string verifier, string challenge) Pkce()
    {
        var v = OpaqueToken.New(32);
        return (v, OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(v))));
    }

    private static string? Q(string url, string key)
    {
        var i = url.IndexOf('?');
        return i < 0 ? null : QueryHelpers.ParseQuery(url[(i + 1)..]).TryGetValue(key, out var v) ? v.ToString() : null;
    }

    private static async Task RegisterAsync(string clientId)
        => await new OAuthClient { Id = clientId, ClientName = "C", RedirectUris = new() { Redirect }, CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);

    private async Task<string> RunToCodeAsync(HttpClient client, string clientId, string challenge, string resource)
    {
        (await client.GetAsync("/test/signin", Ct)).EnsureSuccessStatusCode();
        var url = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = Redirect,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["scope"] = Scope, ["resource"] = resource,
        });
        var rid = Q((await client.GetAsync(url, Ct)).Headers.Location!.ToString(), "rid");
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{rid}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var approveDoc = JsonDocument.Parse(await (await client.SendAsync(approve, Ct)).Content.ReadAsStringAsync(Ct));
        return Q(approveDoc.RootElement.GetProperty("redirect").GetString()!, "code")!;
    }

    [Fact]
    public async Task A_code_minted_for_one_client_cannot_be_redeemed_by_another()
    {
        await RegisterAsync("xc-a");
        await RegisterAsync("xc-b");
        var (verifier, challenge) = Pkce();
        using var client = _fx.NewClient();
        var code = await RunToCodeAsync(client, "xc-a", challenge, Resource);

        // redeem the code bound to xc-a as xc-b
        var res = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect,
            ["client_id"] = "xc-b", ["code_verifier"] = verifier,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task A_device_code_cannot_be_redeemed_under_a_different_client_id()
    {
        await RegisterAsync("dc-a");
        await RegisterAsync("dc-b");
        using var device = _fx.NewClient();
        var devRes = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "dc-a", ["scope"] = Scope, ["resource"] = Resource,
        }), Ct);
        var deviceCode = JsonDocument.Parse(await devRes.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("device_code").GetString()!;

        var res = await device.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = deviceCode, ["client_id"] = "dc-b",
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Remembered_consent_does_not_apply_to_a_different_resource()
    {
        await RegisterAsync("rr-client");
        var (verifier, challenge) = Pkce();
        using var client = _fx.NewClient();

        // first connect for resource A → grant created (and consume the code to complete the flow)
        var codeA = await RunToCodeAsync(client, "rr-client", challenge, Resource);
        await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = codeA, ["redirect_uri"] = Redirect,
            ["client_id"] = "rr-client", ["code_verifier"] = verifier,
        }), Ct);

        // authorize for a DIFFERENT resource B (same client + scope), still signed in → must NOT skip consent
        var (_, challengeB) = Pkce();
        var url = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = "rr-client", ["redirect_uri"] = Redirect,
            ["code_challenge"] = challengeB, ["code_challenge_method"] = "S256", ["scope"] = Scope,
            ["resource"] = "https://other.example/mcp",
        });
        var res = await client.GetAsync(url, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().StartWith("/me/connect", "a grant for resource A must not auto-approve resource B");
    }

    [Fact]
    public async Task State_is_echoed_on_an_error_redirect()
    {
        await RegisterAsync("st-client");
        using var client = _fx.NewClient();
        var url = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = "st-client", ["redirect_uri"] = Redirect,
            ["resource"] = Resource, ["state"] = "opaque-state-42", // no PKCE → error redirect
        });
        var res = await client.GetAsync(url, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loc = res.Headers.Location!.ToString();
        Q(loc, "error").Should().Be("invalid_request");
        Q(loc, "state").Should().Be("opaque-state-42");
    }
}
