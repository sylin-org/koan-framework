using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0025 — the MCP edge on the shared resource mechanism: a token whose audience is the MCP resource with a trailing
/// slash reaches the transport; a token issued for the application's own pages (its base URL) is not the MCP resource's
/// and is refused with <c>error="invalid_token"</c>, as is a token signed under another issuer; no token is a plain
/// challenge that still points at the protected-resource metadata.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class McpEdgeResourceSpec : IClassFixture<McpAuthRampFixture>
{
    private readonly McpAuthRampFixture _fx;
    public McpEdgeResourceSpec(McpAuthRampFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private async Task<HttpResponseMessage> Stream(string? token)
    {
        using var client = _fx.NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Quick);
    }

    private string Mint(string audience)
        => _fx.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "alice" }, TimeSpan.FromMinutes(5), audience);

    [Fact]
    public async Task A_token_for_the_mcp_resource_with_a_trailing_slash_reaches_the_transport()
    {
        using var res = await Stream(Mint($"{_fx.BaseUrl}/mcp/"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_for_the_application_pages_or_another_issuer_is_refused_as_invalid_token()
    {
        using var pages = await Stream(Mint(_fx.BaseUrl));
        using var foreign = await Stream(OAuthTestFlows.Sign(_fx.Services, "https://elsewhere.example", $"{_fx.BaseUrl}/mcp"));

        foreach (var res in new[] { pages, foreign })
        {
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"")
                .And.Contain($"resource_metadata=\"{_fx.BaseUrl}/.well-known/oauth-protected-resource/mcp\"");
        }
    }

    [Fact]
    public async Task No_token_is_a_plain_challenge_pointing_at_the_metadata()
    {
        using var res = await Stream(token: null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Be($"Bearer resource_metadata=\"{_fx.BaseUrl}/.well-known/oauth-protected-resource/mcp\"");
    }
}
