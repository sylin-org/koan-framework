using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Mcp.Streamable.IntegrationTests.StreamableTestHelpers;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// AI-0037 D-C / review finding 2 — when auth is required, the dual-purpose <c>GET /mcp</c> gates ONLY the stream
/// branch; the console stays anonymous (the discoverable human face must not 401).
/// </summary>
public sealed class StreamableAuthWithExplorerSpec : IClassFixture<StreamableAuthWithExplorerFixture>
{
    private const string Route = "/mcp";
    private readonly StreamableAuthWithExplorerFixture _fx;

    public StreamableAuthWithExplorerSpec(StreamableAuthWithExplorerFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task The_console_is_anonymous_even_when_authentication_is_required()
    {
        using var client = _fx.NewClient();
        using var req = GetRequest(Route, accept: "text/html,application/xhtml+xml,*/*;q=0.8");
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK, "the console is the anonymous-discoverable human face");
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task The_stream_branch_of_the_same_route_is_bearer_gated()
    {
        using var client = _fx.NewClient();
        using var req = GetRequest(Route, accept: "text/event-stream"); // no bearer
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }
}
