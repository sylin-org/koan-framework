using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN1 (docs/assessment/09 §8 · AN-cards) — the serialized MCP tool object must match the MCP spec wire
/// shape: <c>inputSchema</c> (camelCase) + a dedicated <c>annotations</c> object. Koan historically
/// emitted <c>input_schema</c> (snake_case) + a non-spec <c>metadata</c> bag, so hints placed in
/// <c>metadata</c> were silently ignored by spec-compliant clients — which blocks AN4 (verb-derived
/// annotations). This holds for an entity tool AND a custom <c>[McpTool]</c> verb.
/// </summary>
public sealed class WireShapeConformanceSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public WireShapeConformanceSpec(ConformanceFixture fx) => _fx = fx;

    [Fact]
    public async Task Entity_tool_uses_spec_inputSchema_and_annotations()
    {
        var toolName = _fx.ResolveToolName("gadget", EntityEndpointOperationKind.GetById);
        var tool = await _fx.GetWireToolAsync(toolName);

        tool.Should().NotBeNull("the entity get-by-id tool is listed");
        AssertSpecShape(tool!);
    }

    [Fact]
    public async Task Custom_verb_uses_spec_inputSchema_and_annotations()
    {
        var tool = await _fx.GetWireToolAsync("gadget_ping");

        tool.Should().NotBeNull("the custom [McpTool] verb is listed");
        AssertSpecShape(tool!);
        tool!["inputSchema"]!["properties"]!["dry_run"].Should().BeNull(
            "a custom tool not declared as a mutation must not advertise mutation rehearsal");
    }

    [Fact]
    public async Task Output_schema_slot_is_absent_when_empty_not_misnamed()
    {
        // AN1 leaves an outputSchema slot for AN-structured to fill; until then it must be omitted, never
        // emitted as null or under a non-spec name.
        var toolName = _fx.ResolveToolName("gadget", EntityEndpointOperationKind.GetById);
        var tool = await _fx.GetWireToolAsync(toolName);

        tool!.ContainsKey("output_schema").Should().BeFalse("the slot, if present, uses the spec name outputSchema");
        if (tool.TryGetValue("outputSchema", out var output))
        {
            output.Type.Should().Be(JTokenType.Object, "an emitted outputSchema is a JSON Schema object, never null");
        }
    }

    [Fact]
    public async Task Entity_results_use_the_application_camelCase_contract()
    {
        var toolName = _fx.ResolveToolName("gadget", EntityEndpointOperationKind.GetNew);
        var call = await _fx.CallToolAsync(toolName, null);
        var payload = JObject.Parse(McpHarnessFixtureBase.ContentText(call) ?? "{}");

        payload.ContainsKey("name").Should().BeTrue();
        payload.ContainsKey("quantity").Should().BeTrue();
        payload.ContainsKey("Name").Should().BeFalse();
        payload.ContainsKey("Quantity").Should().BeFalse();
    }

    [Fact]
    public async Task Custom_tool_inputs_and_results_share_the_application_contract()
    {
        var call = await _fx.CallToolAsync("gadget_shape", new JObject
        {
            ["request"] = new JObject
            {
                ["displayName"] = "Desk lamp",
                ["itemCount"] = 3
            }
        });
        var payload = JObject.Parse(McpHarnessFixtureBase.ContentText(call) ?? "{}");

        payload["displayName"]?.Value<string>().Should().Be("Desk lamp");
        payload["itemCount"]?.Value<int>().Should().Be(3);
        payload.ContainsKey("DisplayName").Should().BeFalse();
        payload.ContainsKey("InternalNote").Should().BeFalse();
        payload.ContainsKey("internalNote").Should().BeFalse("[McpIgnore(Output)] applies to custom results too");
    }

    private static void AssertSpecShape(JObject tool)
    {
        tool.ContainsKey("inputSchema").Should().BeTrue("the MCP spec names it inputSchema (camelCase)");
        tool["inputSchema"]!.Type.Should().Be(JTokenType.Object, "inputSchema is a JSON Schema object");
        tool.ContainsKey("input_schema").Should().BeFalse("the non-spec snake_case name must be gone");

        tool.ContainsKey("annotations").Should().BeTrue("the MCP spec carries hints in a dedicated annotations object");
        tool["annotations"]!.Type.Should().Be(JTokenType.Object, "annotations is an object (ToolAnnotations)");
    }
}
