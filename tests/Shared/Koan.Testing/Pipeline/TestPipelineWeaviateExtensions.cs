using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineWeaviateExtensions
{
    public static TestPipeline UsingWeaviateContainer(this TestPipeline pipeline, string key = "weaviate", string dockerKey = "docker")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<WeaviateContainerFixture>(key, _ => ValueTask.FromResult(new WeaviateContainerFixture(dockerKey)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("weaviate.fixture.ready", new
            {
                available = fixture.IsAvailable,
                endpoint = fixture.Endpoint,
                reason = fixture.UnavailableReason,
                dockerKey
            });
        });
    }
}
