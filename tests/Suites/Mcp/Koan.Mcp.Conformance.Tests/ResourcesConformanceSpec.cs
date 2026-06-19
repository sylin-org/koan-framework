using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// P1.2 (docs/assessment/09 §O6) — MCP introspection resources. The server exposes <c>resources/list</c>
/// and <c>resources/read</c>; the framework ships the built-in <c>koan://entities</c> catalog (the
/// entities + verbs the caller may use), projected per grant. These specs go through the real RPC handler
/// (the STDIO-equivalent, local-trust path) via the harness.
/// </summary>
public sealed class ResourcesConformanceSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public ResourcesConformanceSpec(ConformanceFixture fx) => _fx = fx;

    [Fact]
    public async Task Resources_list_includes_the_entity_catalog()
    {
        var resources = await _fx.ListResourcesAsync();
        var uris = resources.OfType<JObject>().Select(r => r["uri"]?.Value<string>()).ToList();

        uris.Should().Contain(EntityCatalogResourceProvider.ResourceUri,
            "the framework ships the koan://entities introspection resource");
        var catalog = resources.OfType<JObject>().First(r => r["uri"]?.Value<string>() == EntityCatalogResourceProvider.ResourceUri);
        catalog["mimeType"]?.Value<string>().Should().Be("application/json");
        catalog["name"]?.Value<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Reading_the_catalog_returns_the_projected_entities_and_verbs()
    {
        var contents = await _fx.ReadResourceAsync(EntityCatalogResourceProvider.ResourceUri);
        contents.Should().NotBeNull("koan://entities is readable");
        contents!["uri"]?.Value<string>().Should().Be(EntityCatalogResourceProvider.ResourceUri);
        contents["mimeType"]?.Value<string>().Should().Be("application/json");

        var doc = JObject.Parse(contents["text"]!.Value<string>()!);
        var entities = (JArray)doc["entities"]!;
        var gadget = entities.OfType<JObject>().FirstOrDefault(e => e["name"]?.Value<string>() == "gadget");
        gadget.Should().NotBeNull("the gadget entity is exposed over MCP and appears in the catalog");

        var verbs = (JArray)gadget!["verbs"]!;
        verbs.OfType<JObject>().Select(v => v["operation"]?.Value<string>())
            .Should().Contain("GetById", "the catalog lists the entity's verbs");

        // Local-trust (null principal): the scoped vault entity is visible too (STDIO sees everything).
        entities.OfType<JObject>().Select(e => e["name"]?.Value<string>())
            .Should().Contain("vault", "the raw handler is local-trust, so scoped entities appear");
    }

    [Fact]
    public async Task Reading_an_unknown_uri_returns_no_contents()
    {
        var contents = await _fx.ReadResourceAsync("koan://does-not-exist");
        contents.Should().BeNull("an unknown (or unreadable) uri returns no contents — existence is not revealed");
    }

    [Fact]
    public void Catalog_projects_per_grant_for_a_concrete_remote_principal()
    {
        // A concrete (non-null) principal is the REMOTE path: it is projected per grant, NOT local-trust.
        // An anonymous remote caller sees the public entity but never the scoped one (walled-means-silent).
        var provider = new EntityCatalogResourceProvider(
            _fx.Services.GetRequiredService<McpEntityRegistry>(),
            _fx.Services.GetRequiredService<IOptions<McpServerOptions>>());

        var anonymousRemote = new ClaimsPrincipal(new ClaimsIdentity());
        var doc = JObject.Parse(provider.Read(EntityCatalogResourceProvider.ResourceUri, anonymousRemote)!.Text);
        var names = ((JArray)doc["entities"]!).OfType<JObject>().Select(e => e["name"]?.Value<string>()).ToList();

        names.Should().Contain("gadget", "a public entity is visible to an anonymous remote caller");
        names.Should().NotContain("vault", "a scoped entity is omitted for an unscoped remote caller (walled-means-silent)");
    }
}
