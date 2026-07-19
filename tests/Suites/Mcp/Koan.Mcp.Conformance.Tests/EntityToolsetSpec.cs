using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// ARCH-0092 (Phase 1, §B/§G/§H) — the explicit MCP realization class. A bare <c>EntityToolset&lt;T&gt;</c>
/// (no <c>[McpEntity]</c>, no controller) registers the entity and exposes its endpoint verbs as MCP tools,
/// running the SAME governed <c>IEntityEndpointService</c> as the terse attribute path.
/// </summary>
public sealed class EntityToolsetSpec : IClassFixture<ToolsetFixture>
{
    private readonly ToolsetFixture _fx;

    public EntityToolsetSpec(ToolsetFixture fx) => _fx = fx;

    [Fact]
    public void A_bare_EntityToolset_exposes_the_entity_verbs_as_tools()
    {
        // Discovery is purely via SprocketToolset — Sprocket carries no [McpEntity] and has no controller.
        _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Collection).Should().NotBeNullOrEmpty();
        _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Upsert).Should().NotBeNullOrEmpty();
        _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.GetById).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToolHidden_absolutely_removes_a_built_in_verb()
    {
        // [ToolHidden(Delete)] on SprocketToolset → no delete tool exists at all.
        var resolveDelete = () => _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Delete);
        resolveDelete.Should().Throw<System.InvalidOperationException>("the delete verb is hidden");
        _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Collection).Should().NotBeNullOrEmpty("other verbs remain");
    }

    [Fact]
    public async Task ToolDescription_overrides_the_template_description()
    {
        var query = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Query);
        var tool = await _fx.GetWireToolAsync(query);
        tool!["description"]?.Value<string>().Should().Be("Search sprockets by name.",
            "[ToolDescription] overrides the generated template description");
    }

    [Fact]
    public async Task A_custom_McpTool_instance_verb_on_a_toolset_is_discovered_and_invoked()
    {
        var call = await _fx.CallToolAsync("sprocket_echo", new JObject { ["value"] = "hi" });
        McpHarnessFixtureBase.ContentText(call).Should().Be("echo:hi",
            "an instance [McpTool] verb on a toolset is discovered and invoked on a DI-created instance");
    }

    [Fact]
    public async Task A_toolset_only_entity_is_fully_functional_end_to_end()
    {
        var upsert = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(upsert, new JObject { ["model"] = new JObject { ["id"] = "spr-1", ["name"] = "Cog" } });

        var collection = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Collection);
        var result = await _fx.CallToolAsync(collection, new JObject());

        var items = JArray.Parse(McpHarnessFixtureBase.ContentText(result) ?? "[]");
        items.OfType<JObject>().Select(i => i["id"]?.Value<string>())
            .Should().Contain("spr-1", "a toolset-registered entity runs the same governed endpoint service as a [McpEntity] one");
    }
}
