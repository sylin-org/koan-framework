using System.Text.Json;
using Koan.Mcp.Hosting;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// The STDIO transport frames JSON-RPC with System.Text.Json while every MCP payload is modelled with
/// Newtonsoft. That seam shipped broken in 1.0.0: STJ could not construct a <c>JObject</c>, so every
/// <c>tools/call</c> carrying arguments failed with -32602, and serializing one emitted Newtonsoft's
/// internals instead of the value, collapsing advertised schemas to <c>"properties":[[[[]]]]</c>.
///
/// Nothing in the suite reached the wire format, so 80 conformance tests passed over a doorway that no
/// real MCP client could use. These specs pin the seam itself.
/// </summary>
public sealed class StdioWireFormatSpec
{
    // The exact options the STDIO dispatcher frames messages with.
    private static JsonSerializerOptions WireOptions() => StreamJsonRpcTransportDispatcher.WireSerializerOptions;

    [Fact]
    public void Tool_call_arguments_survive_the_wire()
    {
        const string json = """
        {"name":"book.upsert","arguments":{"model":{"title":"Fix Verification","status":"Unread"}}}
        """;

        var parameters = JsonSerializer.Deserialize<McpRpcHandler.ToolsCallParams>(json, WireOptions());

        Assert.NotNull(parameters);
        Assert.Equal("book.upsert", parameters!.Name);
        Assert.NotNull(parameters.Arguments);
        Assert.Equal("Fix Verification", (string?)parameters.Arguments!["model"]?["title"]);
    }

    [Fact]
    public void Absent_arguments_stay_null()
    {
        var parameters = JsonSerializer.Deserialize<McpRpcHandler.ToolsCallParams>(
            """{"name":"book.collection"}""", WireOptions());

        Assert.NotNull(parameters);
        Assert.Null(parameters!.Arguments);
    }

    [Fact]
    public void A_json_object_serializes_as_its_value_not_its_internals()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["title"] = new JObject { ["type"] = "string" } }
        };

        var written = JsonSerializer.Serialize(schema, WireOptions());

        // The 1.0.0 defect produced "type":[] and nested empty arrays here.
        Assert.Contains("\"type\":\"object\"", written);
        Assert.Contains("\"title\"", written);
        Assert.DoesNotContain("[[", written);
    }

    [Fact]
    public void A_json_object_round_trips()
    {
        var original = new JObject { ["tools"] = new JObject { ["listChanged"] = false } };

        var written = JsonSerializer.Serialize(original, WireOptions());
        var read = JsonSerializer.Deserialize<JObject>(written, WireOptions());

        Assert.NotNull(read);
        Assert.False((bool)read!["tools"]!["listChanged"]!);
    }
}
