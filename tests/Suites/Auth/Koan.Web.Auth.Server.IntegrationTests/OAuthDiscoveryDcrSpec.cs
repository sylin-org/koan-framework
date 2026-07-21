using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 3 — discovery (RFC 8414 AS metadata + OIDC mirror + the public ES256 JWKS) and Dynamic Client
/// Registration (RFC 7591, D5 zero-trust: public-only, loopback-only redirects). A DCR'd client is then accepted
/// at /oauth/authorize, proving discovery + registration integrate with the Phase 2 flow.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthDiscoveryDcrSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public OAuthDiscoveryDcrSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task Authorization_server_metadata_advertises_the_endpoints_and_pkce_s256()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/.well-known/oauth-authorization-server", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        var root = doc.RootElement;

        root.GetProperty("issuer").GetString().Should().Be(_fx.BaseUrl);
        root.GetProperty("authorization_endpoint").GetString().Should().Be($"{_fx.BaseUrl}/oauth/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Be($"{_fx.BaseUrl}/oauth/token");
        root.GetProperty("registration_endpoint").GetString().Should().Be($"{_fx.BaseUrl}/oauth/register");
        root.GetProperty("jwks_uri").GetString().Should().Be($"{_fx.BaseUrl}/.well-known/jwks.json");
        root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).Should().Equal("S256");
        root.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString()).Should().Equal("none");
    }

    [Fact]
    public async Task Openid_configuration_mirror_is_served()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/.well-known/openid-configuration", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("issuer").GetString().Should().Be(_fx.BaseUrl);
    }

    [Fact]
    public async Task Jwks_publishes_public_es256_keys_only()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/.well-known/jwks.json", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));

        var keys = doc.RootElement.GetProperty("keys").EnumerateArray().ToArray();
        keys.Should().NotBeEmpty();
        var jwk = keys[0];
        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("P-256");
        jwk.GetProperty("alg").GetString().Should().Be("ES256");
        jwk.GetProperty("x").GetString().Should().NotBeNullOrEmpty();
        jwk.TryGetProperty("d", out _).Should().BeFalse("the private component must never be published");
    }

    [Fact]
    public async Task Dynamic_registration_mints_a_public_client_usable_at_authorize()
    {
        using var client = _fx.NewClient();
        const string redirect = "http://127.0.0.1:9123/callback";

        var reg = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Some MCP Client",
            redirect_uris = new[] { redirect },
        }, Ct);
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        using var regDoc = JsonDocument.Parse(await reg.Content.ReadAsStringAsync(Ct));
        regDoc.RootElement.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        regDoc.RootElement.TryGetProperty("client_secret", out _).Should().BeFalse("a dynamic client is public");
        var clientId = regDoc.RootElement.GetProperty("client_id").GetString();
        clientId.Should().NotBeNullOrEmpty();

        // The registered client is immediately usable: authorize accepts it + its loopback redirect.
        var authorizeUrl = QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["code_challenge"] = "abc123challengeabc123challengeabc123challenge",
            ["code_challenge_method"] = "S256",
            ["resource"] = $"{_fx.BaseUrl}/mcp",
        });
        var authorizeRes = await client.GetAsync(authorizeUrl, Ct);
        authorizeRes.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorizeRes.Headers.Location!.ToString().Should().StartWith("/me/connect");
    }

    [Fact]
    public async Task Dynamic_registration_rejects_a_non_loopback_redirect()
    {
        using var client = _fx.NewClient();
        var reg = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Phisher",
            redirect_uris = new[] { "https://evil.example/steal" },
        }, Ct);

        reg.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reg.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_redirect_uri");
    }
}

/// <summary>SEC-0006 D5/D8 — the fixed-window rate limiter that bounds the open registration/device endpoints.</summary>
public sealed class FixedWindowRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Allows_up_to_the_limit_then_throttles_within_the_window()
    {
        var limiter = new FixedWindowRateLimiter();
        var window = TimeSpan.FromMinutes(1);

        limiter.TryAcquire("k", 3, window, T0).Should().BeTrue();
        limiter.TryAcquire("k", 3, window, T0).Should().BeTrue();
        limiter.TryAcquire("k", 3, window, T0).Should().BeTrue();
        limiter.TryAcquire("k", 3, window, T0).Should().BeFalse("the 4th call exceeds the limit of 3");
    }

    [Fact]
    public void Resets_after_the_window_elapses()
    {
        var limiter = new FixedWindowRateLimiter();
        var window = TimeSpan.FromMinutes(1);

        limiter.TryAcquire("k", 1, window, T0).Should().BeTrue();
        limiter.TryAcquire("k", 1, window, T0).Should().BeFalse();
        limiter.TryAcquire("k", 1, window, T0 + TimeSpan.FromMinutes(2)).Should().BeTrue("a new window resets the count");
    }

    [Fact]
    public void Separate_keys_are_independent()
    {
        var limiter = new FixedWindowRateLimiter();
        var window = TimeSpan.FromMinutes(1);

        limiter.TryAcquire("a", 1, window, T0).Should().BeTrue();
        limiter.TryAcquire("b", 1, window, T0).Should().BeTrue("a different key has its own bucket");
    }
}
