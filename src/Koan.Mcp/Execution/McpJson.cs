using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Execution;

/// <summary>Single JSON policy for application-owned MCP inputs and outputs.</summary>
internal static class McpJson
{
    public static JsonSerializer CreateApplicationSerializer()
        => JsonSerializer.Create(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = McpContractResolver.Instance
        });

    public static JToken FromApplicationObject(object? value)
        => value is null ? JValue.CreateNull() : JToken.FromObject(value, CreateApplicationSerializer());
}
