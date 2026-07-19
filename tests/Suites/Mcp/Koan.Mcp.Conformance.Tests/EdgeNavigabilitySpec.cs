using System.Linq;
using System.Security.Claims;
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

    private async Task Upsert(string entity, JObject model, ClaimsPrincipal? user = null)
    {
        var tool = _fx.ResolveToolName(entity, EntityEndpointOperationKind.Upsert);
        var args = new JObject { ["model"] = model };
        if (user is null) await _fx.CallToolAsync(tool, args);
        else await _fx.CallToolAsAsync(tool, args, user);
    }

    [Fact]
    public async Task Following_an_edge_recipe_returns_exactly_the_related_rows()
    {
        // SEC-0004 Phase 3.3b: `article` is an [Access]-gated entity (articles:read) — the unified gate now
        // governs DATA, not just visibility, so seed + traverse it AS an authorized caller. (An anonymous STDIO
        // data call would be correctly denied; `author` is public, so it stays anonymous.)
        var reader = McpHarnessFixtureBase.Principal("articles:read");
        await Upsert("author", new JObject { ["id"] = "an7-nav-author", ["name"] = "Ada" });
        await Upsert("article", new JObject { ["id"] = "an7-nav-a", ["title"] = "Mine", ["authorId"] = "an7-nav-author" }, reader);
        await Upsert("article", new JObject { ["id"] = "an7-nav-b", ["title"] = "Theirs", ["authorId"] = "an7-nav-other" }, reader);

        // Navigate the author→article (via AuthorId) edge using the target's own governed Collection tool.
        var collection = _fx.ResolveToolName("article", EntityEndpointOperationKind.Collection);
        var filter = new JObject { ["authorId"] = "an7-nav-author" }.ToString(Formatting.None);
        var result = await _fx.CallToolAsAsync(collection, new JObject { ["filter"] = filter }, reader);

        var items = JArray.Parse(McpHarnessFixtureBase.ContentText(result) ?? "[]");
        var ids = items.OfType<JObject>().Select(a => a["id"]?.Value<string>()).ToList();

        ids.Should().Contain("an7-nav-a", "the edge resolves to the rows whose via-field matches the source id");
        ids.Should().NotContain("an7-nav-b", "rows of the other author are not on this edge");
    }
}
