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
/// SEC-0039 end-to-end: the MCP edge has no authentication of its own. A token from the embedded AS is for the
/// application, read at <c>/mcp</c> by the application's resource edge as on any route; a token for anything else is
/// refused there; a closed door refuses nobody-at-all as the application's authentication does, and no OAuth metadata
/// names the edge.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class McpAuthRampSpec : IClassFixture<McpAuthRampFixture>
{
    private readonly McpAuthRampFixture _fx;

    public McpAuthRampSpec(McpAuthRampFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private HttpClient Plain() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_fx.BaseUrl) };

    [Fact]
    public async Task The_edge_publishes_no_protected_resource_metadata()
    {
        using var client = Plain();

        var res = await client.GetAsync("/.well-known/oauth-protected-resource/mcp", Quick);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nobody_at_a_closed_door_is_refused_as_the_applications_authentication_refuses_them()
    {
        using var client = Plain();

        var res = await client.GetAsync("/mcp/capabilities", Quick);

        res.IsSuccessStatusCode.Should().BeFalse();
        res.Headers.WwwAuthenticate.ToString().Should().NotContain("resource_metadata");
    }

    [Fact]
    public async Task A_token_for_another_resource_is_refused_by_the_resource_edge()
    {
        var wrongAud = _fx.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "alice", Roles = new[] { "admin" } },
            TimeSpan.FromMinutes(5), audience: "koan://some/other/resource");
        using var client = Plain();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/capabilities");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongAud);

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
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
    public async Task DevToken_mints_a_token_for_the_application_that_reaches_the_mcp_edge()
    {
        using var client = _fx.NewClient();
        (await client.GetAsync("/test/signin", Quick)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenRes = await client.GetAsync("/oauth/dev-token", Quick);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(Quick));
        tokenDoc.RootElement.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokenDoc.RootElement.GetProperty("resource").GetString().Should().Be(_fx.BaseUrl);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        using var anonymous = Plain();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/capabilities");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var edgeRes = await anonymous.SendAsync(req, Quick);

        edgeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        edgeRes.Headers.Should().NotContain(h => h.Key == "WWW-Authenticate");
        edgeRes.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }
}
