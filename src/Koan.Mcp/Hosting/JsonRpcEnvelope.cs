using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// A parsed JSON-RPC request envelope — the transport-agnostic unit every MCP HTTP surface dispatches through
/// <see cref="McpRpcDispatcher"/>. <see cref="TryParse"/> is the single shared parser (AI-0037 — one parse path), so
/// a malformed body is rejected identically on the Streamable endpoint and the legacy <c>/rpc</c> shim.
/// </summary>
public sealed record JsonRpcEnvelope(string Jsonrpc, string Method, JToken? Params, JToken? Id)
{
    public static bool TryParse(JToken node, out JsonRpcEnvelope envelope, out string? error)
    {
        envelope = default!;
        error = null;

        if (node is not JObject obj)
        {
            error = "Payload must be a JSON object.";
            return false;
        }

        var methodNode = obj["method"];
        if (methodNode?.Type != JTokenType.String || methodNode.Value<string>() is not { Length: > 0 } method)
        {
            error = "Missing method.";
            return false;
        }

        var jsonRpc = obj["jsonrpc"]?.Value<string>() ?? "2.0";
        var parameters = obj.TryGetValue("params", out var paramsNode) ? paramsNode : null;
        var id = obj.TryGetValue("id", out var idNode) ? idNode : null;

        envelope = new JsonRpcEnvelope(jsonRpc, method, parameters, id);
        return true;
    }
}
