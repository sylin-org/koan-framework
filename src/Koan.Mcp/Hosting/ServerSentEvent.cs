using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

public readonly record struct ServerSentEvent(string Event, JToken Payload)
{
    public static ServerSentEvent Connected(string sessionId, DateTimeOffset timestamp)
        => new("connected", new JObject
        {
            ["sessionId"] = sessionId,
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent Heartbeat(DateTimeOffset timestamp)
        => new("heartbeat", new JObject
        {
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent Acknowledged(JToken? id)
    {
        var payload = new JObject
        {
            ["id"] = id != null ? id.DeepClone() : JValue.CreateNull()
        };
        return new("ack", payload);
    }

    public static ServerSentEvent Completed(DateTimeOffset timestamp)
        => new("end", new JObject
        {
            ["timestamp"] = timestamp.ToString("O")
        });

    public static ServerSentEvent FromJsonRpc(JObject message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        var cloned = (JObject)message.DeepClone();
        var eventName = cloned.ContainsKey("error") ? "error" : "result";
        return new(eventName, cloned);
    }

    public string ToWireFormat()
    {
        var builder = new StringBuilder();
        builder.Append("event: ").Append(Event).Append('\n');
        var json = Payload?.ToString(Formatting.None) ?? "null";
        builder.Append("data: ").Append(json).Append("\n\n");
        return builder.ToString();
    }
}

internal static class HttpSseHeaders
{
    public const string SessionId = "X-Mcp-Session";
}
