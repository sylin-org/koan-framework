using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots a scope-gated and an origin:local-gated entity for the STDIO trust cells.</summary>
public sealed class StdioTrustFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options)
    {
        options.AllowedEntities.Add("vault");
        options.AllowedEntities.Add("lockbox");
    }
}

/// <summary>
/// SEC-0017: STDIO acts as one principal, anonymous and <c>origin:local</c>, for tools/list, resources and calls.
/// A verb it lists is a verb it can call.
/// </summary>
public sealed class StdioTrustSpec : IClassFixture<StdioTrustFixture>
{
    private readonly StdioTrustFixture _fx;

    public StdioTrustSpec(StdioTrustFixture fx) => _fx = fx;

    [Fact]
    public async Task A_scope_gated_entity_is_neither_listed_nor_in_the_catalog()
    {
        var vaultRead = _fx.ResolveToolName("vault", EntityEndpointOperationKind.Collection);
        var call = await _fx.CallToolAsync(vaultRead, new JObject());
        McpHarnessFixtureBase.IsError(call).Should().BeTrue("the anonymous STDIO caller lacks the scope: {0}", call);

        var names = (await _fx.ListToolsAsync()).Select(t => t["name"]!.Value<string>()).ToArray();
        names.Should().NotContain(vaultRead, "tools/list must not advertise what every STDIO call is denied");

        var catalog = JObject.Parse((await _fx.ReadResourceAsync(EntityCatalogResourceProvider.ResourceUri))!["text"]!.Value<string>()!);
        ((JArray)catalog["entities"]!).OfType<JObject>().Select(e => e["name"]!.Value<string>()).Should().NotContain("vault");
    }

    [Fact]
    public async Task An_origin_local_entity_is_listed_and_callable()
    {
        var lockboxRead = _fx.ResolveToolName("lockbox", EntityEndpointOperationKind.Collection);

        (await _fx.ListToolsAsync()).Select(t => t["name"]!.Value<string>()).Should().Contain(lockboxRead);
        McpHarnessFixtureBase.IsError(await _fx.CallToolAsync(lockboxRead, new JObject())).Should().BeFalse();
    }
}
