using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// Proves that generated MCP entity tools cannot bypass the Data-owned Lifecycle boundary.
/// </summary>
public sealed class LifecycleParitySpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fixture;

    public LifecycleParitySpec(ConformanceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mcp_upsert_runs_the_same_lifecycle_as_direct_data_writes()
    {
        var id = $"lifecycle-{Guid.CreateVersion7():n}";
        var upsert = _fixture.ResolveToolName("gadget", EntityEndpointOperationKind.Upsert);

        await _fixture.CallToolAsync(upsert, new JObject
        {
            ["model"] = new JObject
            {
                ["id"] = id,
                ["name"] = "  agent intent  ",
                ["quantity"] = 1,
            },
        });

        var stored = await Gadget.Get(id);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("AGENT INTENT");
    }
}
