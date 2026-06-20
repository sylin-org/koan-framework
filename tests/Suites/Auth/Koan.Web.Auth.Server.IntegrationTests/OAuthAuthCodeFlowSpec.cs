using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
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
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 2 — the Authorization Code + PKCE flow end-to-end over a real host: authorize → consent seam →
/// approve → token. Plus the D4 integrity negatives: PKCE mismatch, single-use replay, mandatory PKCE,
/// exact redirect-uri match, and the D7 browser-binding on approval.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthAuthCodeFlowSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public OAuthAuthCodeFlowSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";
    private const string RedirectUri = "http://127.0.0.1:7777/callback";

    private static (string verifier, string challenge) Pkce()
    {
        var verifier = OpaqueToken.New(32);
        var challenge = OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static async Task<string> RegisterClientAsync(string clientId, string redirectUri)
    {
        var client = new OAuthClient
        {
            Id = clientId,
            ClientName = "Test MCP Client",
            RedirectUris = new() { redirectUri },
            IsPublic = true,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        await client.Save(Ct);
        return clientId;
    }

    private static string? QueryParam(string url, string key)
    {
        var i = url.IndexOf('?');
        if (i < 0) return null;
        return QueryHelpers.ParseQuery(url[(i + 1)..]).TryGetValue(key, out var v) ? v.ToString() : null;
    }

    /// <summary>Drives authorize → consent → approve and returns the issued authorization code.</summary>
    private async Task<string> RunToCodeAsync(HttpClient client, string clientId, string challenge, string state)
    {
        (await client.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var authorizeUrl = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "mcp.read mcp.write",
            ["resource"] = Resource,
            ["state"] = state,
        });
        var authorizeRes = await client.GetAsync(authorizeUrl, Ct);
        authorizeRes.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var consentLocation = authorizeRes.Headers.Location!.ToString();
        consentLocation.Should().StartWith("/me/connect");
        var rid = QueryParam(consentLocation, "rid");
        rid.Should().NotBeNullOrEmpty();

        // consent context
        var ctxRes = await client.GetAsync($"/oauth/request/{rid}", Ct);
        ctxRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ctxDoc = JsonDocument.Parse(await ctxRes.Content.ReadAsStringAsync(Ct));
        ctxDoc.RootElement.GetProperty("user").GetProperty("loggedIn").GetBoolean().Should().BeTrue();
        ctxDoc.RootElement.GetProperty("resource").GetString().Should().Be(Resource);

        // approve (fetch-style → JSON { redirect })
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{rid}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var approveRes = await client.SendAsync(approve, Ct);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var approveDoc = JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync(Ct));
        var redirect = approveDoc.RootElement.GetProperty("redirect").GetString()!;
        redirect.Should().StartWith(RedirectUri);
        QueryParam(redirect, "state").Should().Be(state);
        var code = QueryParam(redirect, "code");
        code.Should().NotBeNullOrEmpty();
        return code!;
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpClient client, string clientId, string code, string verifier, string redirectUri)
        => await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }), Ct);

    [Fact]
    public async Task Full_authorization_code_pkce_flow_issues_an_aud_bound_token()
    {
        var clientId = await RegisterClientAsync("client-happy", RedirectUri);
        var (verifier, challenge) = Pkce();
        using var client = _fx.NewClient();

        var code = await RunToCodeAsync(client, clientId, challenge, "state-123");

        var tokenRes = await TokenAsync(client, clientId, code, verifier, RedirectUri);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("token_type").GetString().Should().Be("Bearer");
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Header.Alg.Should().Be("ES256");
        jwt.Audiences.Should().Contain(Resource);                              // D2 audience binding
        jwt.Subject.Should().Be("alice");
        jwt.Claims.Where(c => c.Type == "role" || c.Type.EndsWith("/role")).Select(c => c.Value)
            .Should().Contain("admin", "roles come from the session, not the request (D6)");
    }

    [Fact]
    public async Task A_replayed_code_is_rejected_single_use()
    {
        var clientId = await RegisterClientAsync("client-replay", RedirectUri);
        var (verifier, challenge) = Pkce();
        using var client = _fx.NewClient();
        var code = await RunToCodeAsync(client, clientId, challenge, "s");

        (await TokenAsync(client, clientId, code, verifier, RedirectUri)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await TokenAsync(client, clientId, code, verifier, RedirectUri);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await replay.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task A_wrong_pkce_verifier_is_rejected()
    {
        var clientId = await RegisterClientAsync("client-pkce", RedirectUri);
        var (_, challenge) = Pkce();
        using var client = _fx.NewClient();
        var code = await RunToCodeAsync(client, clientId, challenge, "s");

        var (otherVerifier, _) = Pkce(); // a verifier that does NOT match the challenge
        var res = await TokenAsync(client, clientId, code, otherVerifier, RedirectUri);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Authorize_without_pkce_redirects_an_error()
    {
        var clientId = await RegisterClientAsync("client-nopkce", RedirectUri);
        using var client = _fx.NewClient();

        var url = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["resource"] = Resource,
            ["state"] = "s",
        });
        var res = await client.GetAsync(url, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loc = res.Headers.Location!.ToString();
        loc.Should().StartWith(RedirectUri);
        QueryParam(loc, "error").Should().Be("invalid_request");
    }

    [Fact]
    public async Task Authorize_with_an_unregistered_redirect_uri_is_rejected_without_redirecting()
    {
        var clientId = await RegisterClientAsync("client-redir", RedirectUri);
        using var client = _fx.NewClient();

        var url = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = "http://evil.example/steal",
            ["code_challenge"] = Pkce().challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = Resource,
        });
        var res = await client.GetAsync(url, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        res.Headers.Location.Should().BeNull("an unregistered redirect_uri must never be redirected to");
    }

    [Fact]
    public async Task Approve_from_a_different_browser_is_rejected_browser_binding()
    {
        var clientId = await RegisterClientAsync("client-binding", RedirectUri);
        var (_, challenge) = Pkce();
        using var initiator = _fx.NewClient();

        (await initiator.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var authorizeUrl = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = Resource,
        });
        var rid = QueryParam((await initiator.GetAsync(authorizeUrl, Ct)).Headers.Location!.ToString(), "rid");

        // a DIFFERENT browser (no binding cookie), even though it's signed in, cannot approve
        using var attacker = _fx.NewClient();
        (await attacker.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{rid}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var res = await attacker.SendAsync(approve, Ct);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
