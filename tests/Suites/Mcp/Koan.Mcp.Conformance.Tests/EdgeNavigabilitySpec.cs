using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN7 (docs/assessment/09 §5) — the edge is a route, and the route is resolved by the ALREADY-GOVERNED
/// Query tool (not a new per-edge verb). This spec proves the catalog's edge recipe (target + via field) is
/// actionable end-to-end: filtering the target entity tool by the edge's via-field returns exactly the
/// related rows — the navigation the catalog advertises, run through the one governed read path.
/// </summary>
public sealed class EdgeNavigabilitySpec : IClassFixture<EdgeFixture>
{
    private readonly EdgeFixture _fx;

    public EdgeNavigabilitySpec(EdgeFixture fx) => _fx = fx;

    private async Task Upsert(string entity, JObject model)
    {
        var tool = _fx.ResolveToolName(entity, EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(tool, new JObject { ["model"] = model });
    }

    [Fact]
    public async Task Following_an_edge_recipe_returns_exactly_the_related_rows()
    {
        await Upsert("author", new JObject { ["id"] = "an7-nav-author", ["name"] = "Ada" });
        await Upsert("article", new JObject { ["id"] = "an7-nav-a", ["title"] = "Mine", ["authorId"] = "an7-nav-author" });
        await Upsert("article", new JObject { ["id"] = "an7-nav-b", ["title"] = "Theirs", ["authorId"] = "an7-nav-other" });

        // Navigate the author→article (via AuthorId) edge using the target's own governed Collection tool.
        var collection = _fx.ResolveToolName("article", EntityEndpointOperationKind.Collection);
        var filter = new JObject { ["AuthorId"] = "an7-nav-author" }.ToString(Formatting.None);
        var result = await _fx.CallToolAsync(collection, new JObject { ["filter"] = filter });

        var items = JArray.Parse(McpHarnessFixtureBase.ContentText(result) ?? "[]");
        var ids = items.OfType<JObject>().Select(a => a["Id"]?.Value<string>()).ToList();

        ids.Should().Contain("an7-nav-a", "the edge resolves to the rows whose via-field matches the source id");
        ids.Should().NotContain("an7-nav-b", "rows of the other author are not on this edge");
    }
}
