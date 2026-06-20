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
/// AI-0037 + SEC-0006 — the authenticated transport: the <c>Mcp-Session-Id</c> is not a bearer capability. A session
/// established by one principal cannot be driven by another authenticated caller who learns its id.
/// </summary>
public sealed class StreamableAuthSpec : IClassFixture<StreamableAuthFixture>
{
    private const string Route = "/mcp";
    private readonly StreamableAuthFixture _fx;

    public StreamableAuthSpec(StreamableAuthFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    private static object InitParams => new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "streamable-auth-spec", version = "1.0" },
    };

    /// <summary>Mint an ES256 token for <paramref name="subject"/>, bound to this host's MCP resource (RFC 8707 aud).</summary>
    private string Token(string subject)
    {
        var issuer = _fx.Services.GetRequiredService<IAsymmetricIssuer>();
        return issuer.Issue(new TrustClaims { Subject = subject }, TimeSpan.FromMinutes(5), audience: _fx.Resource);
    }

    private async Task<string> InitializeAs(HttpClient client, string token)
    {
        using var req = PostRequest(Route, Rpc("initialize", id: 1, @params: InitParams));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK, "an aud-bound bearer reaches the edge and initialize mints the session");
        res.Headers.TryGetValues(SessionHeader, out var ids).Should().BeTrue();
        return System.Linq.Enumerable.Single(ids!);
    }

    [Fact]
    public async Task The_session_owner_can_drive_the_session()
    {
        using var client = _fx.NewClient();
        var alice = Token("alice");
        var sessionId = await InitializeAs(client, alice);

        using var req = PostRequest(Route, Rpc("tools/list", id: 2), sessionId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alice);
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_different_principal_cannot_drive_another_principals_session()
    {
        using var client = _fx.NewClient();
        var sessionId = await InitializeAs(client, Token("alice"));

        // Bob is fully authenticated (valid, aud-bound) but is NOT the session owner → 403, not execution under alice.
        using var req = PostRequest(Route, Rpc("tools/list", id: 2), sessionId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("bob"));
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
