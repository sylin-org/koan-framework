using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>Minimal client-side helpers for driving the MCP Streamable HTTP endpoint over real HTTP.</summary>
public static class StreamableTestHelpers
{
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
    public static HttpRequestMessage PostRequest(string route, string body,
        string? accept = "application/json, text/event-stream", string? protocolVersion = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(req, accept, protocolVersion);
        return req;
    }

    public static HttpRequestMessage GetRequest(string route, string? accept = "text/event-stream")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, route);
        ApplyHeaders(req, accept, null);
        return req;
    }

    private static void ApplyHeaders(HttpRequestMessage req, string? accept, string? protocolVersion)
    {
        if (accept is not null)
        {
            foreach (var part in accept.Split(','))
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(part.Trim()));
        }
        if (protocolVersion is not null) req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
    }
}
