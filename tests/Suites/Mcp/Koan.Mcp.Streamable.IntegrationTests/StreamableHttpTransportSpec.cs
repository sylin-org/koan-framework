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
/// AI-0037, SEC-0039 — the MCP Streamable HTTP transport end-to-end over real Kestrel, with no session: each POST stands
/// alone and is answered as one JSON object, a notification is 202, content negotiation and the protocol version are
/// checked, and a GET or a DELETE is told there is nothing to open or end.
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

    private static async Task<JsonDocument> Json(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Quick));
    }

    [Fact]
    public async Task Initialize_answers_plain_json_with_no_session_and_negotiates_the_protocol_version()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("initialize", id: 1, @params: InitParams));

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.Contains("Mcp-Session-Id").Should().BeFalse("there is no session");
        using var doc = await Json(res);
        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
        result.TryGetProperty("serverInfo", out _).Should().BeTrue();
    }

    /// <summary>The TypeScript SDK asks for the revision it knows best and then says what it was answered on every request.</summary>
    [Theory]
    [InlineData("2025-11-25", "2025-11-25")]
    [InlineData("1999-01-01", "2025-11-25")]
    [InlineData("2025-03-26", "2025-03-26")]
    public async Task Initialize_answers_a_version_it_speaks_and_the_next_request_may_say_it(string asked, string answered)
    {
        using var client = _fx.NewClient();
        using var init = PostRequest(Route, Rpc("initialize", id: 1, @params: new
        {
            protocolVersion = asked,
            capabilities = new { },
            clientInfo = new { name = "negotiation-test", version = "1.0.0" },
        }));
        var opened = await client.SendAsync(init, Quick);
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await Json(opened);
        doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be(answered);

        using var next = PostRequest(Route, Rpc("notifications/initialized"), protocolVersion: answered);
        var answer = await client.SendAsync(next, Quick);

        answer.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Tools_list_stands_alone_and_returns_the_entity_tools()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await Json(res);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);
        tools.EnumerateArray().Select(t => t.GetProperty("name").GetString())
            .Should().Contain(n => n!.Contains("gizmo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_session_id_a_client_still_sends_means_nothing()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2));
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", "deadbeefdeadbeefdeadbeefdeadbeef");

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await Json(res);
        doc.RootElement.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_notification_is_acked_202_with_no_body()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("notifications/initialized")); // no id → notification

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
    public async Task An_unsupported_protocol_version_is_400()
    {
        using var client = _fx.NewClient();
        using var req = PostRequest(Route, Rpc("tools/list", id: 2), protocolVersion: "1999-01-01");
        var res = await client.SendAsync(req, Quick);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The specification's ping answers an empty object, and a client's schema refuses anything else.</summary>
    [Fact]
    public async Task Ping_answers_empty()
    {
        using var client = _fx.NewClient();
        using var ping = PostRequest(Route, Rpc("ping", id: 2), protocolVersion: "2025-06-18");

        var answer = await client.SendAsync(ping, Quick);

        using var doc = await Json(answer);
        doc.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("result").EnumerateObject().Should().BeEmpty();
    }

    /// <summary>An unknown tool is an invalid parameter (-32602) in the specification; -32601 says the method is what it does not know.</summary>
    [Fact]
    public async Task An_unknown_tool_is_an_invalid_parameter()
    {
        using var client = _fx.NewClient();
        using var call = PostRequest(Route, Rpc("tools/call", id: 2, @params: new { name = "no_such_tool", arguments = new { } }), protocolVersion: "2025-06-18");

        var answer = await client.SendAsync(call, Quick);

        using var doc = await Json(answer);
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task A_get_is_told_there_is_no_stream()
    {
        using var client = _fx.NewClient();
        using var req = GetRequest(Route);

        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        res.Content.Headers.Allow.Should().Equal("POST");
    }

    [Fact]
    public async Task A_delete_has_no_session_to_end()
    {
        using var client = _fx.NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Delete, Route);
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", "deadbeefdeadbeefdeadbeefdeadbeef");

        var res = await client.SendAsync(req, Quick);

        res.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
