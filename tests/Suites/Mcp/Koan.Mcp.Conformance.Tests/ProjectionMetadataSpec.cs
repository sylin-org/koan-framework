using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004 (§C) — the MCP edge attaches the per-row capability manifest (<c>can:[]</c>) to the tool-result
/// metadata BY DEFAULT (agents need it to plan; MCP carries structured metadata natively, so unlike REST it does
/// not wait for an opt-in). An open, unconstrained entity advertises every verb — allow-by-default made honest.
/// </summary>
public sealed class ProjectionMetadataSpec : IClassFixture<ToolsetFixture>
{
    private readonly ToolsetFixture _fx;

    public ProjectionMetadataSpec(ToolsetFixture fx) => _fx = fx;

    [Fact]
    public async Task A_collection_tool_result_carries_the_per_row_access_manifest_by_default()
    {
        var upsert = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(upsert, new JObject { ["model"] = new JObject { ["id"] = "spr-proj-1", ["name"] = "Widget" } });

        var collection = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Collection);
        var result = await _fx.CallToolAsync(collection, new JObject());

        var access = result["meta"]?["diagnostics"]?["access"] as JObject;
        access.Should().NotBeNull("the MCP edge attaches the can:[] manifest to the tool-result metadata by default");

        var can = access!["spr-proj-1"]?["can"] as JArray;
        can.Should().NotBeNull("each listed row carries its permitted verbs");
        can!.Values<string>().Should().BeEquivalentTo(new[] { "read", "write", "remove" },
            "an open, unconstrained entity advertises every verb — allow-by-default made honest");
    }
}
