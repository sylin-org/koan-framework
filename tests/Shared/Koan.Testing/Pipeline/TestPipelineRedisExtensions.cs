using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineRedisExtensions
{
    public static TestPipeline UsingRedisContainer(this TestPipeline pipeline, string key = "redis", string dockerKey = "docker")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<RedisContainerFixture>(key, _ => ValueTask.FromResult(new RedisContainerFixture(dockerKey)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("redis.fixture.ready", new
            {
                available = fixture.IsAvailable,
                connectionString = fixture.ConnectionString,
                reason = fixture.UnavailableReason,
                dockerKey
            });
        });
    }
}
