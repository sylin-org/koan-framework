using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Data.Core.Relationships;
using Koan.Mcp;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN7 (docs/assessment/09 §5) — governed edge traversal as discovery. Relationships are already declared
/// (<c>[Parent]</c>); the projector READS them and exposes the navigable graph in the <c>koan://entities</c>
/// catalog as terse edge descriptors — so the whole graph is navigable WITHOUT any edge becoming a tool
/// (no catalog explosion). An edge is a route, never a verb. Per-grant: an edge to a WALLED target type is
/// ABSENT (walled-means-silent — the field name would otherwise leak the target's existence). The actual
/// traversal runs through the already-governed Query tool (the edge carries the recipe: target + via field).
/// </summary>
public sealed class EdgeTraversalSpec : IClassFixture<EdgeFixture>
{
    private readonly EdgeFixture _fx;

    public EdgeTraversalSpec(EdgeFixture fx) => _fx = fx;

    private async Task<JObject> CatalogEntity(string name)
    {
        var contents = await _fx.ReadResourceAsync(EntityCatalogResourceProvider.ResourceUri);
        var doc = JObject.Parse(contents!["text"]!.Value<string>()!);
        return ((JArray)doc["entities"]!).OfType<JObject>().First(e => e["name"]?.Value<string>() == name);
    }

    private static JArray Edges(JObject entity) => (entity["edges"] as JArray) ?? new JArray();

    [Fact]
    public async Task The_catalog_lists_child_edges_to_a_visible_target()
    {
        var author = await CatalogEntity("author");
        var childEdges = Edges(author).OfType<JObject>()
            .Where(e => e["kind"]?.Value<string>() == "child" && e["target"]?.Value<string>() == "article").ToList();

        childEdges.Select(e => e["via"]?.Value<string>())
            .Should().Contain("AuthorId").And.Contain("EditorId", "both same-target edges are navigable (no collapse)");
        childEdges.Should().OnlyContain(e => e["cardinality"]!.Value<string>() == "many", "a child edge is to-many");
    }

    [Fact]
    public async Task The_catalog_lists_parent_edges()
    {
        var article = await CatalogEntity("article"); // local-trust sees the scoped child
        var parentEdges = Edges(article).OfType<JObject>()
            .Where(e => e["kind"]?.Value<string>() == "parent" && e["target"]?.Value<string>() == "author").ToList();

        parentEdges.Select(e => e["via"]?.Value<string>())
            .Should().Contain("AuthorId").And.Contain("EditorId");
        parentEdges.Should().OnlyContain(e => e["cardinality"]!.Value<string>() == "one", "a parent edge is to-one");
    }

    [Fact]
    public async Task An_edge_carries_the_navigation_recipe()
    {
        var author = await CatalogEntity("author");
        var edge = Edges(author).OfType<JObject>()
            .First(e => e["kind"]?.Value<string>() == "child" && e["via"]?.Value<string>() == "AuthorId");

        edge["target"]?.Value<string>().Should().Be("article", "the recipe names the target entity tool to query");
        edge["via"]?.Value<string>().Should().Be("AuthorId", "the recipe names the field to filter on");
        edge["name"]?.Value<string>().Should().NotBeNullOrEmpty("the edge has a stable handle");
    }

    [Fact]
    public void An_edge_to_a_walled_target_is_absent_for_an_unscoped_grant()
    {
        // The REMOTE per-grant path: an anonymous caller lacks articles:read, so `article` is walled.
        var provider = new EntityCatalogResourceProvider(
            _fx.Services.GetRequiredService<McpEntityRegistry>(),
            _fx.Services.GetRequiredService<IRelationshipMetadata>(),
            _fx.Services.GetRequiredService<IOptions<McpServerOptions>>());

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var doc = JObject.Parse(provider.Read(EntityCatalogResourceProvider.ResourceUri, anonymous)!.Text);
        var entities = (JArray)doc["entities"]!;

        entities.OfType<JObject>().Select(e => e["name"]?.Value<string>())
            .Should().NotContain("article", "a scoped entity is walled for an unscoped caller");

        var author = entities.OfType<JObject>().First(e => e["name"]?.Value<string>() == "author");
        Edges(author).OfType<JObject>().Should().NotContain(e => e["target"]!.Value<string>() == "article",
            "an edge to a walled target is absent — the field name would otherwise leak the target's existence");
    }
}
