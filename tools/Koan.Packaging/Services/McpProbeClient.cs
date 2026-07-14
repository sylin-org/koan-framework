using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class McpProbeClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? sessionId;
    private int requestId;

    public async Task InitializeAsync(string clientName, CancellationToken cancellationToken)
    {
        using var request = Request("initialize", new
        {
            protocolVersion = PackagingConstants.ApplicationProbe.McpProtocolVersion,
            capabilities = new { },
            clientInfo = new { name = clientName, version = "1.0" }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!response.Headers.TryGetValues(PackagingConstants.ApplicationProbe.McpSessionHeader, out var values))
            throw new InvalidOperationException("MCP initialize did not return a session identifier.");
        sessionId = values.Single();
        _ = await ReadRpcAsync(response, cancellationToken);
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        if (sessionId is null) throw new InvalidOperationException("Initialize the MCP probe before making calls.");
        using var request = Request(method, parameters, sessionId);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRpcAsync(response, cancellationToken);
    }

    public async Task<JsonElement> CallToolAsync(string name, object? arguments, CancellationToken cancellationToken)
    {
        var rpc = await CallAsync("tools/call", new { name, arguments }, cancellationToken);
        var result = rpc.GetProperty("result");
        if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean())
            throw new InvalidOperationException($"MCP tool '{name}' returned an error: {result}");
        return result;
    }

    public static string ToolText(JsonElement result) =>
        result.GetProperty("content").EnumerateArray()
            .First(content => content.GetProperty("type").GetString() == "text")
            .GetProperty("text").GetString() ?? "";

    private HttpRequestMessage Request(string method, object? parameters, string? currentSession = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = ++requestId
        };
        if (parameters is not null) body["params"] = parameters;

        var request = new HttpRequestMessage(HttpMethod.Post, PackagingConstants.ApplicationProbe.McpPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (currentSession is not null)
            request.Headers.TryAddWithoutValidation(PackagingConstants.ApplicationProbe.McpSessionHeader, currentSession);
        return request;
    }

    private static async Task<JsonElement> ReadRpcAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? ParseSseData(body)
            : body;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ParseSseData(string body)
    {
        var data = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line[5..].TrimStart())
            .ToArray();
        if (data.Length == 0) throw new InvalidOperationException("MCP returned an empty SSE response.");
        return string.Join('\n', data);
    }
}
