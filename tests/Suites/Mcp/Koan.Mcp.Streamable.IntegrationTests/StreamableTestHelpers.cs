using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>One parsed Server-Sent Event frame: its <c>id:</c> (the resumption cursor) and joined <c>data:</c>.</summary>
public readonly record struct SseEvent(string? Id, string? EventName, string Data);

/// <summary>Minimal client-side helpers for driving the MCP Streamable HTTP endpoint over real HTTP.</summary>
public static class StreamableTestHelpers
{
    public const string SessionHeader = "Mcp-Session-Id";

    /// <summary>Build a JSON-RPC request/notification body. A null <paramref name="id"/> makes it a notification.</summary>
    public static string Rpc(string method, object? id = null, object? @params = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"jsonrpc\":\"2.0\",\"method\":\"").Append(method).Append('"');
        if (id is not null) sb.Append(",\"id\":").Append(RenderId(id));
        if (@params is not null) sb.Append(",\"params\":").Append(System.Text.Json.JsonSerializer.Serialize(@params));
        sb.Append('}');
        return sb.ToString();
    }

    private static string RenderId(object id) => id switch
    {
        int i => i.ToString(),
        long l => l.ToString(),
        _ => "\"" + id + "\"",
    };

    /// <summary>POST a single JSON-RPC message. By default advertises both representations (the spec requirement).</summary>
    public static HttpRequestMessage PostRequest(string route, string body, string? sessionId = null,
        string? accept = "application/json, text/event-stream", string? protocolVersion = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(req, sessionId, accept, protocolVersion);
        return req;
    }

    public static HttpRequestMessage GetRequest(string route, string? sessionId = null,
        string? accept = "text/event-stream", string? lastEventId = null, string? protocolVersion = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, route);
        ApplyHeaders(req, sessionId, accept, protocolVersion);
        if (lastEventId is not null) req.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        return req;
    }

    private static void ApplyHeaders(HttpRequestMessage req, string? sessionId, string? accept, string? protocolVersion)
    {
        if (accept is not null)
        {
            foreach (var part in accept.Split(','))
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(part.Trim()));
        }
        if (sessionId is not null) req.Headers.TryAddWithoutValidation(SessionHeader, sessionId);
        if (protocolVersion is not null) req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
    }

    /// <summary>Read an entire (already-completed) SSE body — used for per-request POST responses, which close.</summary>
    public static IReadOnlyList<SseEvent> ParseSse(string body)
    {
        var events = new List<SseEvent>();
        string? id = null, ev = null;
        var data = new StringBuilder();
        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                if (data.Length > 0 || id is not null || ev is not null)
                {
                    events.Add(new SseEvent(id, ev, data.ToString()));
                    id = null; ev = null; data.Clear();
                }
                continue;
            }
            if (rawLine.StartsWith("id:", StringComparison.Ordinal)) id = rawLine[3..].Trim();
            else if (rawLine.StartsWith("event:", StringComparison.Ordinal)) ev = rawLine[6..].Trim();
            else if (rawLine.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(rawLine[5..].TrimStart());
            }
        }
        if (data.Length > 0 || id is not null || ev is not null) events.Add(new SseEvent(id, ev, data.ToString()));
        return events;
    }

    /// <summary>Read exactly one SSE event off a live (never-closing) stream, with the caller's timeout.</summary>
    public static async Task<SseEvent> ReadOneEvent(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? id = null, ev = null;
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) throw new EndOfStreamException("SSE stream closed before an event arrived.");
            if (line.Length == 0)
            {
                if (data.Length > 0 || id is not null || ev is not null)
                    return new SseEvent(id, ev, data.ToString());
                continue;
            }
            if (line.StartsWith("id:", StringComparison.Ordinal)) id = line[3..].Trim();
            else if (line.StartsWith("event:", StringComparison.Ordinal)) ev = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }
    }
}
