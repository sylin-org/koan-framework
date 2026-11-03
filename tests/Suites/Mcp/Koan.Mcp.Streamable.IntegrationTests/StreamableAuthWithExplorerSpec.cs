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
/// AI-0037 D-C / review finding 2 — when auth is required, the console on <c>GET /mcp</c> stays anonymous (the
/// discoverable human face must not 401).
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

    /// <summary>SEC-0039: there is no stream to refuse a credential for, so none is asked for.</summary>
    [Fact]
    public async Task A_stream_get_on_the_same_route_is_told_there_is_none_and_asked_for_no_credential()
    {
        using var client = _fx.NewClient();
        using var req = GetRequest(Route, accept: "text/event-stream"); // no bearer
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        res.Headers.WwwAuthenticate.Should().BeEmpty();
    }
}
