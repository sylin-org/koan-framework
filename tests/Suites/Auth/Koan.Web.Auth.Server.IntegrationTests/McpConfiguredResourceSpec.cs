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

/// <summary>SEC-0006 D2 — the configured canonical resource id is authoritative; the request Host is not trusted.</summary>
public sealed class McpConfiguredResourceSpec : IClassFixture<McpConfiguredResourceFixture>
{
    private readonly McpConfiguredResourceFixture _fx;

    public McpConfiguredResourceSpec(McpConfiguredResourceFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    [Fact]
    public async Task Metadata_advertises_the_configured_resource_not_the_request_host()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/.well-known/oauth-protected-resource/mcp", Quick);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Quick));
        doc.RootElement.GetProperty("resource").GetString().Should().Be(McpConfiguredResourceFixture.CanonicalResource);
    }

    [Fact]
    public async Task A_token_bound_to_the_configured_resource_is_accepted()
    {
        var issuer = _fx.Services.GetRequiredService<IAsymmetricIssuer>();
        var token = issuer.Issue(new TrustClaims { Subject = "alice", Roles = new[] { "admin" } },
            TimeSpan.FromMinutes(5), audience: McpConfiguredResourceFixture.CanonicalResource);

        using var client = _fx.NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_bound_to_the_spoofable_host_derived_resource_is_rejected()
    {
        // The host-derived audience ({base}/mcp) must NOT be accepted when a canonical ResourceUri is configured.
        var issuer = _fx.Services.GetRequiredService<IAsymmetricIssuer>();
        var token = issuer.Issue(new TrustClaims { Subject = "alice", Roles = new[] { "admin" } },
            TimeSpan.FromMinutes(5), audience: $"{_fx.BaseUrl}/mcp");

        using var client = _fx.NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }
}
