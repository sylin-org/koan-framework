using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// WEB-0078 — with <c>Koan:Web:ApplicationUrl=https://www.example.com/tangent</c> and nothing else configured, the
/// authorization server's issuer, the session cookie and the MCP resource identity all carry the path, and the
/// well-known documents answer only under it.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class BaseUrlPathSpec : IClassFixture<BaseUrlPathFixture>
{
    private readonly BaseUrlPathFixture _fx;
    public BaseUrlPathSpec(BaseUrlPathFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task The_issuer_and_every_advertised_endpoint_live_under_the_base_url()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync($"{BaseUrlPathFixture.PathBase}/.well-known/oauth-authorization-server", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        var root = doc.RootElement;

        root.GetProperty("issuer").GetString().Should().Be(BaseUrlPathFixture.ApplicationUrl);
        root.GetProperty("authorization_endpoint").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/oauth/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/oauth/token");
        root.GetProperty("jwks_uri").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/.well-known/jwks.json");
    }

    [Fact]
    public async Task Nothing_answers_at_the_root()
    {
        using var client = _fx.NewClient();
        (await client.GetAsync("/.well-known/oauth-authorization-server", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{BaseUrlPathFixture.PathBase}/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_session_cookie_issued_by_the_host_is_scoped_to_the_path()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync($"{BaseUrlPathFixture.PathBase}/test/signin", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        res.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var session = cookies!.Should().ContainSingle(cookie => cookie.StartsWith(".AspNetCore.Koan.cookie=", StringComparison.Ordinal)).Subject;
        session.Split(';').Select(part => part.Trim()).Should().Contain($"path={BaseUrlPathFixture.PathBase}");
    }

    [Fact]
    public async Task The_mcp_resource_and_its_authorization_server_derive_from_the_base_url()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync($"{BaseUrlPathFixture.PathBase}/.well-known/oauth-protected-resource/mcp", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));

        doc.RootElement.GetProperty("resource").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/mcp");
        doc.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(BaseUrlPathFixture.ApplicationUrl);
    }

    [Fact]
    public async Task A_dev_token_minted_under_the_path_is_bound_to_the_resource_the_edge_enforces()
    {
        using var client = _fx.NewCookieClient();
        (await client.GetAsync($"{BaseUrlPathFixture.PathBase}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenRes = await client.GetAsync($"{BaseUrlPathFixture.PathBase}/oauth/dev-token", Ct);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(Ct));
        tokenDoc.RootElement.GetProperty("resource").GetString().Should().Be($"{BaseUrlPathFixture.ApplicationUrl}/mcp",
            "the default audience is the identity the MCP edge enforces, not the request host");
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{BaseUrlPathFixture.PathBase}/mcp");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        req.Content = new System.Net.Http.StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"spec","version":"1"}}}""",
            System.Text.Encoding.UTF8, "application/json");
        var res = await client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, Ct);

        res.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        res.Headers.Should().NotContain(h => h.Key == "WWW-Authenticate");
    }

    [Fact]
    public async Task The_mcp_challenge_points_at_metadata_under_the_base_url()
    {
        using var client = _fx.NewClient();
        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{BaseUrlPathFixture.PathBase}/mcp");
        req.Content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var res = await client.SendAsync(req, Ct);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should()
            .Contain($"resource_metadata=\"{BaseUrlPathFixture.ApplicationUrl}/.well-known/oauth-protected-resource/mcp\"");
    }
}
