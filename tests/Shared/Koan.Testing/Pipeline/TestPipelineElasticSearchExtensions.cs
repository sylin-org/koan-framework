using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineElasticSearchExtensions
{
    public static TestPipeline UsingElasticSearchContainer(this TestPipeline pipeline, string key = "elasticsearch", string dockerKey = "docker")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<ElasticSearchContainerFixture>(key, _ => ValueTask.FromResult(new ElasticSearchContainerFixture(dockerKey)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("elasticsearch.fixture.ready", new
            {
                available = fixture.IsAvailable,
                endpoint = fixture.Endpoint,
                reason = fixture.UnavailableReason,
                dockerKey
            });
        });
    }
}
