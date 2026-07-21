using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 Phase 1 end-to-end (D2/D3): the MCP edge is an OAuth resource server, and the embedded AS mints a
/// real ES256 token that reaches its gates. Proves the protected-resource discovery (RFC 9728), the bearer +
/// audience enforcement (RFC 8707 confused-deputy fix), and the dev-token on-ramp from a cookie session.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class McpAuthRampSpec : IClassFixture<McpAuthRampFixture>
{
    private readonly McpAuthRampFixture _fx;

    public McpAuthRampSpec(McpAuthRampFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    [Fact]
    public async Task Protected_resource_metadata_is_public_and_points_at_the_AS()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/.well-known/oauth-protected-resource/mcp", Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Quick));
        doc.RootElement.GetProperty("resource").GetString().Should().Be($"{_fx.BaseUrl}/mcp");
        doc.RootElement.GetProperty("authorization_servers")[0].GetString().Should().Be(_fx.BaseUrl);
    }

    [Fact]
    public async Task Mcp_without_a_token_challenges_with_resource_metadata()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/mcp/sse", HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = res.Headers.WwwAuthenticate.ToString();
        challenge.Should().Contain("Bearer");
        challenge.Should().Contain($"resource_metadata=\"{_fx.BaseUrl}/.well-known/oauth-protected-resource/mcp\"");
    }

    [Fact]
    public async Task Mcp_rejects_a_token_bound_to_the_wrong_resource()
    {
        // Authentic (signed by the live ES256 issuer) but bound to a different resource → confused-deputy reject.
        var issuer = _fx.Services.GetRequiredService<IIssuer>();
        var wrongAud = issuer.Issue(new TrustClaims { Subject = "alice", Roles = new[] { "admin" } },
            TimeSpan.FromMinutes(5), audience: "koan://some/other/resource");

        using var client = _fx.NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongAud);
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }

    [Fact]
    public async Task DevToken_requires_a_cookie_session()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/oauth/dev-token", Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await res.Content.ReadAsStringAsync(Quick)).Should().Contain("login_required");
    }

    [Fact]
    public async Task DevToken_mints_an_aud_bound_token_that_reaches_the_mcp_edge()
    {
        using var client = _fx.NewClient();

        // 1. establish a cookie session
        (await client.GetAsync("/test/signin", Quick)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. mint a dev-token for the current user, bound to this host's MCP resource
        var tokenRes = await client.GetAsync("/oauth/dev-token", Quick);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(Quick));
        tokenDoc.RootElement.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokenDoc.RootElement.GetProperty("resource").GetString().Should().Be($"{_fx.BaseUrl}/mcp");
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        // 3. present it to the MCP edge — auth + audience PASS, so we reach the live SSE transport.
        //    ResponseHeadersRead is intentional: an SSE response remains open and must not be buffered to EOF.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var edgeRes = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        edgeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        edgeRes.Headers.Should().NotContain(h => h.Key == "WWW-Authenticate");
        edgeRes.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }
}
