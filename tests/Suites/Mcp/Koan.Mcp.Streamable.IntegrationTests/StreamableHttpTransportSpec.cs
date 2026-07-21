using System;
using System.Linq;
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
/// AI-0037 — the MCP Streamable HTTP transport (spec 2025-06-18) end-to-end over real Kestrel: the single-endpoint
/// POST/GET/DELETE contract, session lifecycle (mint on initialize, resolve, terminate), content negotiation, the
/// per-request SSE response, the single standalone GET stream, and Last-Event-ID resumption replay.
/// </summary>
public sealed class StreamableHttpTransportSpec : IClassFixture<StreamableHttpFixture>
{
    private const string Route = "/mcp";
    private readonly StreamableHttpFixture _fx;

    public StreamableHttpTransportSpec(StreamableHttpFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    private static object InitParams => new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "streamable-spec", version = "1.0" },
    };

    /// <summary>POST initialize and return (sessionId, the parsed initialize SSE event).</summary>
    private async Task<(string SessionId, SseEvent Event)> Initialize(HttpClient client)
    {
        using var req = PostRequest(Route, Rpc("initialize", id: 1, @params: InitParams));
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        res.Headers.TryGetValues(SessionHeader, out var ids).Should().BeTrue("initialize must mint a session id");
        var sessionId = ids!.Single();
        var body = await res.Content.ReadAsStringAsync(Quick);
        var evt = ParseSse(body).Single();
        return (sessionId, evt);
    }

    [Fact]
    public async Task Initialize_mints_a_session_and_negotiates_the_protocol_version()
    {
        using var client = _fx.NewClient();
        var (sessionId, evt) = await Initialize(client);

        sessionId.Should().MatchRegex("^[0-9a-f]{32}$");
        using var doc = JsonDocument.Parse(evt.Data);
        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
        result.TryGetProperty("serverInfo", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Tools_list_within_the_session_returns_the_entity_tools()
    {
        using var client = _fx.NewClient();
        var (sessionId, _) = await Initialize(client);

        using var req = PostRequest(Route, Rpc("tools/list", id: 2), sessionId);
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var evt = ParseSse(await res.Content.ReadAsStringAsync(Quick)).Single();
        using var doc = JsonDocument.Parse(evt.Data);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);
        tools.EnumerateArray().Select(t => t.GetProperty("name").GetString())
            .Should().Contain(n => n!.Contains("gizmo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_notification_is_acked_202_with_no_body()
    {
        using var client = _fx.NewClient();
        var (sessionId, _) = await Initialize(client);

        using var req = PostRequest(Route, Rpc("notifications/initialized"), sessionId); // no id → notification
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await res.Content.ReadAsStringAsync(Quick)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_post_that_does_not_accept_event_stream_is_406()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("initialize", id: 1, @params: InitParams), accept: "application/json");
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);
    }

    [Fact]
    public async Task A_non_initialize_post_without_a_session_id_is_400_not_initialized()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Quick));
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32000);
    }

    [Fact]
    public async Task An_unknown_session_is_404_the_reinit_signal()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2), sessionId: "deadbeefdeadbeefdeadbeefdeadbeef");
        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Quick));
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32001);
    }

    [Fact]
    public async Task An_unsupported_protocol_version_is_400()
    {
        using var client = _fx.NewClient();
        var (sessionId, _) = await Initialize(client);

        using var req = PostRequest(Route, Rpc("tools/list", id: 2), sessionId, protocolVersion: "1999-01-01");
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_terminates_the_session_then_it_is_unknown()
    {
        using var client = _fx.NewClient();
        var (sessionId, _) = await Initialize(client);

        using var del = GetRequestlessDelete(sessionId);
        var delRes = await client.SendAsync(del, Quick);
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var after = PostRequest(Route, Rpc("tools/list", id: 3), sessionId);
        var afterRes = await client.SendAsync(after, Quick);
        afterRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_one_standalone_GET_stream_is_allowed_per_session()
    {
        using var client = _fx.NewClient();
        var (sessionId, _) = await Initialize(client);

        // Open the first GET stream and hold it (headers read; body keeps streaming).
        using var get1 = GetRequest(Route, sessionId);
        var res1 = await client.SendAsync(get1, HttpCompletionOption.ResponseHeadersRead, Quick);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        res1.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        // A second concurrent standalone GET is rejected.
        using var get2 = GetRequest(Route, sessionId);
        var res2 = await client.SendAsync(get2, HttpCompletionOption.ResponseHeadersRead, Quick);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        res1.Dispose();
    }

    [Fact]
    public async Task A_resumption_GET_replays_the_named_streams_buffered_tail()
    {
        using var client = _fx.NewClient();
        var (sessionId, initEvt) = await Initialize(client);

        // The initialize response rode the first per-request stream; its event id is "r1.1". Resume from "r1.0" so
        // the replay includes that event, proving Last-Event-ID → per-stream tail replay over HTTP.
        initEvt.Id.Should().NotBeNull();
        var streamId = initEvt.Id!.Split('.')[0];
        var lastEventId = $"{streamId}.0";

        using var get = GetRequest(Route, sessionId, lastEventId: lastEventId);
        var res = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await res.Content.ReadAsStreamAsync(Quick);
        var replayed = await ReadOneEvent(stream, Quick);

        replayed.Id.Should().Be(initEvt.Id);
        using var doc = JsonDocument.Parse(replayed.Data);
        doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
    }

    private static HttpRequestMessage GetRequestlessDelete(string sessionId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, Route);
        req.Headers.TryAddWithoutValidation(SessionHeader, sessionId);
        return req;
    }
}
