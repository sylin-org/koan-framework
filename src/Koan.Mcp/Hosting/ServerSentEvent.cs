using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Koan.Mcp.Hosting;

public readonly record struct ServerSentEvent(string Event, JsonNode Payload)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static ServerSentEvent Connected(string sessionId, DateTimeOffset timestamp)
        => new("connected", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent Heartbeat(DateTimeOffset timestamp)
        => new("heartbeat", new JsonObject
        {
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent Acknowledged(JsonNode? id)
    {
        var payload = new JsonObject();
        if (id is not null)
        {
            payload["id"] = id.DeepClone();
        }
        else
        {
            payload["id"] = null;
        }

        return new ServerSentEvent("ack", payload);
    }

    public static ServerSentEvent Completed(DateTimeOffset timestamp)
        => new("end", new JsonObject
        {
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent FromJsonRpc(JsonObject message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        var cloned = (JsonObject)message.DeepClone();
        var eventName = cloned.ContainsKey("error") ? "error" : "result";
        return new ServerSentEvent(eventName, cloned);
    }

    public string ToWireFormat()
    {
        var builder = new StringBuilder();
        builder.Append("event: ").Append(Event).Append('\n');
        var json = Payload?.ToJsonString(SerializerOptions) ?? "null";
        builder.Append("data: ").Append(json).Append("\n\n");
        return builder.ToString();
    }
}

internal static class HttpSseHeaders
{
    public const string SessionId = "X-Mcp-Session";
}
