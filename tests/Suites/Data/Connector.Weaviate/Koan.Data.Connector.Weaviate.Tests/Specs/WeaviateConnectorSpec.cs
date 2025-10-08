using Koan.Data.Core;
using Koan.Data.Connector.Weaviate;
using Koan.TestPipeline;

namespace Koan.Data.Connector.Weaviate.Tests.Specs;

public class WeaviateConnectorSpec : IClassFixture<WeaviateConnectorFixture>
{
    private readonly WeaviateConnectorFixture _fixture;

    public WeaviateConnectorSpec(WeaviateConnectorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Weaviate: Vector CRUD and search (blocked: connector discovery updates)")]
    public async Task VectorCrud_AndSearch_Blocked()
    {
        // TODO: Implement when connector discovery and vector fixtures are ready
        // Assert.True(false, "Connector discovery updates pending");
    }
}
