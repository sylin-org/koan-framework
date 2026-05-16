using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineOpenSearchExtensions
{
    public static TestPipeline UsingOpenSearchContainer(this TestPipeline pipeline, string key = "opensearch", string dockerKey = "docker")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<OpenSearchContainerFixture>(key, _ => ValueTask.FromResult(new OpenSearchContainerFixture(dockerKey)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("opensearch.fixture.ready", new
            {
                available = fixture.IsAvailable,
                endpoint = fixture.Endpoint,
                reason = fixture.UnavailableReason,
                dockerKey
            });
        });
    }
}
