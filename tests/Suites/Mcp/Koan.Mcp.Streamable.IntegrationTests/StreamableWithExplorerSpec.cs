using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Mcp.Streamable.IntegrationTests.StreamableTestHelpers;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// AI-0037 D-C — with the Streamable transport AND the Explorer console both enabled, the single core-owned
/// <c>GET /mcp</c> content-negotiates: a browser reaches the console, an MCP client reaches the stream branch, and
/// the POST endpoint serves Streamable. No <c>AmbiguousMatchException</c> — exactly one route owns the path.
/// </summary>
public sealed class StreamableWithExplorerSpec : IClassFixture<StreamableWithExplorerFixture>
{
    private const string Route = "/mcp";
    private readonly StreamableWithExplorerFixture _fx;

    public StreamableWithExplorerSpec(StreamableWithExplorerFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task A_browser_get_reaches_the_console()
    {
        using var client = _fx.NewClient();
        using var req = GetRequest(Route, accept: "text/html,application/xhtml+xml,*/*;q=0.8");
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await res.Content.ReadAsStringAsync(Quick)).Should().Contain("MCP Explorer");
    }

    [Fact]
    public async Task An_mcp_client_post_reaches_the_streamable_transport()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("initialize", id: 1, @params: new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "coenabled", version = "1.0" },
        }));
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.TryGetValues(SessionHeader, out _).Should().BeTrue("Streamable initialize mints the session id");
    }

    [Fact]
    public async Task An_mcp_client_get_reaches_the_stream_branch_not_the_console()
    {
        using var client = _fx.NewClient();
        // A stream GET with no session routes to the Streamable stream branch (400 missing-session), NOT the console
        // and NOT an ambiguous-route 500 — proving the single route disambiguates by Accept.
        using var req = GetRequest(Route, accept: "text/event-stream");
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Quick));
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32000);
    }
}
