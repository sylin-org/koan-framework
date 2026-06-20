using System;
using System.Collections.Generic;
using System.Linq;
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
using Koan.Web.Authorization;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 5 (D9) — refresh tokens (rotation + reuse-detection, backed by a revocable grant) and
/// remembered consent. Refresh keeps the client connected past the short access-token lifetime; reuse of a
/// rotated token or revocation of the grant fails closed; a re-connect with a live grant skips the consent page.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthRefreshSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public OAuthRefreshSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";
    private const string Redirect = "http://127.0.0.1:7788/cb";
    private const string Scope = "mcp.read mcp.write";

    private static (string verifier, string challenge) Pkce()
    {
        var v = OpaqueToken.New(32);
        return (v, OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(v))));
    }

    private static string? QueryParam(string url, string key)
    {
        var i = url.IndexOf('?');
        return i < 0 ? null : QueryHelpers.ParseQuery(url[(i + 1)..]).TryGetValue(key, out var v) ? v.ToString() : null;
    }

    private static async Task RegisterClientAsync(string clientId)
    {
        await new OAuthClient { Id = clientId, ClientName = "Refresh Client", RedirectUris = new() { Redirect }, IsPublic = true, CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);
    }

    /// <summary>signin → authorize → approve → token; returns the parsed token response.</summary>
    private async Task<JsonElement> RunFlowAsync(HttpClient client, string clientId)
    {
        var (verifier, challenge) = Pkce();
        (await client.GetAsync("/test/signin", Ct)).EnsureSuccessStatusCode();

        var authorizeUrl = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = Redirect,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = Scope,
            ["resource"] = Resource,
        });
        var authorize = await client.GetAsync(authorizeUrl, Ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var rid = QueryParam(authorize.Headers.Location!.ToString(), "rid");

        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{rid}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var approveRes = await client.SendAsync(approve, Ct);
        using var approveDoc = JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync(Ct));
        var code = QueryParam(approveDoc.RootElement.GetProperty("redirect").GetString()!, "code");

        var tokenRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = Redirect,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }), Ct);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string clientId, string refreshToken)
        => await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }), Ct);

    [Fact]
    public async Task Token_response_includes_a_refresh_token_that_can_be_redeemed()
    {
        await RegisterClientAsync("rt-basic");
        using var client = _fx.NewClient();
        var token = await RunFlowAsync(client, "rt-basic");

        var refresh1 = token.GetProperty("refresh_token").GetString();
        refresh1.Should().NotBeNullOrEmpty();

        var refreshed = await RefreshAsync(client, "rt-basic", refresh1!);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBe(refresh1, "refresh tokens rotate");
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        await RegisterClientAsync("rt-reuse");
        using var client = _fx.NewClient();
        var token = await RunFlowAsync(client, "rt-reuse");
        var refresh1 = token.GetProperty("refresh_token").GetString()!;

        // rotate once
        var r2 = await RefreshAsync(client, "rt-reuse", refresh1);
        var refresh2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("refresh_token").GetString()!;

        // replay the now-rotated refresh1 → reuse detected
        var reuse = await RefreshAsync(client, "rt-reuse", refresh1);
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");

        // the family is revoked: even the legitimately-rotated refresh2 no longer works
        (await RefreshAsync(client, "rt-reuse", refresh2)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoking_the_backing_grant_fails_the_next_refresh()
    {
        await RegisterClientAsync("rt-revoke");
        using var client = _fx.NewClient();
        var token = await RunFlowAsync(client, "rt-revoke");
        var refresh1 = token.GetProperty("refresh_token").GetString()!;

        // revoke the server-side grant (what the user / an admin would do)
        foreach (var g in await AgentGrant.Query(g => g.Subject == "alice", Ct))
            await g.Remove(Ct);

        var refreshed = await RefreshAsync(client, "rt-revoke", refresh1);
        refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Remembered_consent_skips_the_consent_page_on_reconnect()
    {
        await RegisterClientAsync("rt-remember");
        using var client = _fx.NewClient();

        // first connect — full flow creates the grant
        await RunFlowAsync(client, "rt-remember");

        // re-connect: same signed-in browser, same client + scope → authorize goes STRAIGHT to the redirect with a
        // code (NOT to the app consent page).
        var (_, challenge) = Pkce();
        var authorizeUrl = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "rt-remember",
            ["redirect_uri"] = Redirect,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = Scope,
            ["resource"] = Resource,
        });
        var res = await client.GetAsync(authorizeUrl, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loc = res.Headers.Location!.ToString();
        loc.Should().StartWith(Redirect, "remembered consent skips the consent page");
        QueryParam(loc, "code").Should().NotBeNullOrEmpty();
    }
}
