using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Security.Trust.Issuer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Koan.Mcp.Streamable.IntegrationTests.StreamableTestHelpers;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// AI-0037 + SEC-0016, SEC-0039 — the authenticated transport: each request is authorized as the bearer it carries, and
/// nothing a request before it carried is kept.
/// </summary>
public sealed class StreamableAuthSpec : IClassFixture<StreamableAuthFixture>
{
    private const string Route = "/mcp";
    private readonly StreamableAuthFixture _fx;

    public StreamableAuthSpec(StreamableAuthFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    /// <summary>Mint an ES256 token for <paramref name="subject"/>, bound to this host's MCP resource (RFC 8707 aud).</summary>
    private string Token(string subject, params string[] scopes)
    {
        var issuer = _fx.Services.GetRequiredService<IIssuer>();
        return issuer.Issue(new TrustClaims { Subject = subject, Scopes = scopes }, TimeSpan.FromMinutes(5), audience: _fx.Resource);
    }

    [Fact]
    public async Task A_request_with_its_own_bearer_stands_alone()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("alice"));

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The closed door answers as the application's own authentication does, and points at no OAuth metadata of its own.</summary>
    [Fact]
    public async Task A_request_with_no_credential_is_refused_as_the_applications_authentication_refuses_it()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_fx.BaseUrl) };
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));

        var res = await client.SendAsync(req, Quick);

        res.IsSuccessStatusCode.Should().BeFalse();
        res.Headers.WwwAuthenticate.ToString().Should().NotContain("resource_metadata");
        (await client.GetAsync("/.well-known/oauth-protected-resource/mcp", Quick)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bearer_for_another_resource_is_refused_by_the_resource_edge()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            _fx.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "alice" }, TimeSpan.FromMinutes(5), audience: $"{_fx.BaseUrl}/mcp"));

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }

    // SEC-0016: each request runs as the principal it carries. A refreshed token with fewer scopes reduces authority at once.
    [Fact]
    public async Task A_scope_downgrade_between_requests_is_honored()
    {
        using var client = _fx.NewClient();
        var body = Rpc("tools/call", id: 2, @params: new { name = "ledgerbook_upsert", arguments = new { model = new { entry = "x" } } });

        using var wide = PostRequest(Route, body, protocolVersion: "2025-06-18");
        wide.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("carol", "items:read", "items:write"));
        var allowed = await (await client.SendAsync(wide, Quick)).Content.ReadAsStringAsync(Quick);

        using var narrow = PostRequest(Route, body, protocolVersion: "2025-06-18");
        narrow.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("carol", "items:read"));
        var res = await client.SendAsync(narrow, Quick);
        var text = await res.Content.ReadAsStringAsync(Quick);

        allowed.Should().NotContain("\"isError\":true", "the write scope is in the token that request carries");
        res.StatusCode.Should().Be(HttpStatusCode.OK, text);
        text.Should().Contain("\"isError\":true", "the write scope is gone from the token this request carries");
        text.Should().Contain("forbidden");
    }
}
