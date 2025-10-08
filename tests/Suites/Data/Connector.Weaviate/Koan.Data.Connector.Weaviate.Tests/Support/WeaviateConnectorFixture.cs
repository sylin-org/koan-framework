using Koan.TestPipeline;
using Koan.Data.Connector.Weaviate;

namespace Koan.Data.Connector.Weaviate.Tests.Support;

public class WeaviateConnectorFixture : TestPipelineFixture
{
    public WeaviateConnectorFixture()
        : base("weaviate", seedPack: null) // Add seed pack if available
    {
        // Additional setup if needed
    }
}
