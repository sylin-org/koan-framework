using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Mcp.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots an MCP host exposing the two <c>[Door]</c> entities.</summary>
public sealed class DoorEntityFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options)
    {
        options.AllowedEntities.Add("showcase");
        options.AllowedEntities.Add("vaultroom");
    }
}

/// <summary>
/// SEC-0005 (the Door / 09 §8 Wall·Door·Verb) — a <c>[Door]</c> entity discloses a verb the caller cannot invoke as
/// a <c>door</c> (named + how-to-unlock), drift-proof because the <c>needs</c> derives from the same gate that
/// enforces it. A role-gated (privilege) verb stays a silent Wall even with <c>[Door]</c>. STDIO local-trust has no
/// doors (everything is a Verb).
/// </summary>
public sealed class DoorProjectionSpec : IClassFixture<DoorEntityFixture>
{
    private readonly DoorEntityFixture _fx;

    public DoorProjectionSpec(DoorEntityFixture fx) => _fx = fx;

    private EntityCatalogResourceProvider Provider() => new(
        _fx.Services.GetRequiredService<McpEntityRegistry>(),
        _fx.Services.GetRequiredService<Koan.Data.Core.Relationships.IRelationshipMetadata>(),
        _fx.Services.GetRequiredService<Koan.Web.Authorization.IAccessGateCache>());

    private static JObject Entity(JObject doc, string name)
        => ((JArray)doc["entities"]!).OfType<JObject>().First(e => e["name"]?.Value<string>() == name);

    private static string[] Ops(JObject entity, string key)
        => entity[key] is JArray arr ? arr.OfType<JObject>().Select(x => x["operation"]!.Value<string>()!).ToArray() : System.Array.Empty<string>();

    [Fact]
    public void A_door_entity_discloses_a_denied_capability_with_its_needs()
    {
        // A concrete anonymous-remote caller (the per-grant path) lacks showcase:write.
        var doc = JObject.Parse(Provider().Read(EntityCatalogResourceProvider.ResourceUri, new ClaimsPrincipal(new ClaimsIdentity()))!.Text);
        var showcase = Entity(doc, "showcase");

        // The open read verb is callable; the gated write is a DOOR, not a wall.
        Ops(showcase, "verbs").Should().Contain("Collection", "read is open → a callable verb");
        var doors = (JArray)showcase["doors"]!;
        var writeDoor = doors.OfType<JObject>().First(d => d["operation"]!.Value<string>() == "Upsert");
        writeDoor["needs"]!.Value<string>().Should().Be("requires scope:showcase:write",
            "the door signpost is the gate's needs — derived from the same gate that enforces it");
    }

    [Fact]
    public void A_role_gated_verb_stays_a_silent_wall_even_with_door()
    {
        var doc = JObject.Parse(Provider().Read(EntityCatalogResourceProvider.ResourceUri, new ClaimsPrincipal(new ClaimsIdentity()))!.Text);
        var vaultroom = Entity(doc, "vaultroom");

        Ops(vaultroom, "doors").Should().NotContain("Delete", "a role/privilege-gated verb is never disclosed as a door (admin is a Wall)");
        Ops(vaultroom, "verbs").Should().NotContain("Delete", "and it is not callable either — it is a silent wall");
    }

    [Fact]
    public async Task Stdio_local_trust_has_no_doors()
    {
        // The raw handler (null principal) is local-trust: every verb is callable, so no doors are projected.
        var contents = await _fx.ReadResourceAsync(EntityCatalogResourceProvider.ResourceUri);
        var doc = JObject.Parse(contents!["text"]!.Value<string>()!);
        Entity(doc, "showcase")["doors"].Should().BeNull("STDIO local-trust sees everything as a verb — no doors");
    }
}
