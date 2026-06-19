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
    public async Task A_toolset_only_entity_is_fully_functional_end_to_end()
    {
        var upsert = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(upsert, new JObject { ["model"] = new JObject { ["id"] = "spr-1", ["name"] = "Cog" } });

        var collection = _fx.ResolveToolName("sprocket", EntityEndpointOperationKind.Collection);
        var result = await _fx.CallToolAsync(collection, new JObject());

        var items = JArray.Parse(McpHarnessFixtureBase.ContentText(result) ?? "[]");
        items.OfType<JObject>().Select(i => i["Id"]?.Value<string>())
            .Should().Contain("spr-1", "a toolset-registered entity runs the same governed endpoint service as a [McpEntity] one");
    }
}
