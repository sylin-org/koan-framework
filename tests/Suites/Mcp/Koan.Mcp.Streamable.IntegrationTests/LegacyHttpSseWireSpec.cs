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
/// AI-0037 Ph3-pre — the GOLDEN legacy HTTP+SSE wire contract, asserted byte-for-byte against the current
/// transport. This is the regression detector the 3b break-and-rebuild was missing: it pins the exact frame
/// sequence (<c>connected → endpoint → ack → message</c>), the <c>X-Mcp-Session</c> header, the endpoint URL
/// shape, and — critically — the ABSENCE of an <c>id:</c> line on the legacy <c>message</c> frame (which the
/// unified <c>EnqueueMessage</c> would wrongly add). After the collapse this exact spec must still pass.
/// </summary>
public sealed class LegacyHttpSseWireSpec : IClassFixture<LegacyHttpSseFixture>
{
    private readonly LegacyHttpSseFixture _fx;

    public LegacyHttpSseWireSpec(LegacyHttpSseFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    [Fact]
    public async Task The_legacy_sse_rpc_roundtrip_emits_the_golden_frame_sequence()
    {
        using var client = _fx.NewClient();

        // 1. Open the GET /sse stream. The session id rides the X-Mcp-Session response header.
        using var getReq = GetRequest("/mcp/sse");
        var getRes = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, Quick);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        getRes.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        getRes.Headers.TryGetValues("X-Mcp-Session", out var sidValues).Should().BeTrue("legacy mints X-Mcp-Session");
        var sessionId = System.Linq.Enumerable.Single(sidValues!);
        sessionId.Should().MatchRegex("^[0-9a-f]{32}$");

        await using var stream = await getRes.Content.ReadAsStreamAsync(Quick);
        await using var sse = new SseStreamReader(stream); // ONE reader across all frames (no buffer loss)

        // 2. connected — carries the session id.
        var connected = await sse.ReadAsync(Quick);
        connected.EventName.Should().Be("connected");
        using (var doc = JsonDocument.Parse(connected.Data))
            doc.RootElement.GetProperty("sessionId").GetString().Should().Be(sessionId);

        // 3. endpoint — tells the client where to POST (raw URL string, not JSON).
        var endpoint = await sse.ReadAsync(Quick);
        endpoint.EventName.Should().Be("endpoint");
        endpoint.Data.Should().Be($"/mcp/rpc?sessionId={sessionId}");

        // 4. POST /rpc — the legacy submit. Returns 202; the response rides the GET stream.
        using var rpcReq = PostRequest($"/mcp/rpc?sessionId={sessionId}", Rpc("tools/list", id: 1),
            accept: "application/json");
        var rpcRes = await client.SendAsync(rpcReq, Quick);
        rpcRes.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // 5. ack — echoes the request id.
        var ack = await sse.ReadAsync(Quick);
        ack.EventName.Should().Be("ack");
        using (var doc = JsonDocument.Parse(ack.Data))
            doc.RootElement.GetProperty("id").GetInt32().Should().Be(1);

        // 6. message — the JSON-RPC response, on the GET stream, with NO id: line (the golden byte).
        var message = await sse.ReadAsync(Quick);
        message.EventName.Should().Be("message");
        message.Id.Should().BeNull("the legacy message frame carries no SSE id: line");
        using (var doc = JsonDocument.Parse(message.Data))
        {
            doc.RootElement.GetProperty("id").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength().Should().BeGreaterThan(0);
        }
    }
}
