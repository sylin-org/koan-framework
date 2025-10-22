using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineMongoExtensions
{
    public static TestPipeline UsingMongoContainer(this TestPipeline pipeline, string key = "mongo", string dockerKey = "docker", string database = "Koan")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<MongoContainerFixture>(key, _ => ValueTask.FromResult(new MongoContainerFixture(dockerKey, database)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("mongo.fixture.ready", new
            {
                available = fixture.IsAvailable,
                connectionString = fixture.ConnectionString,
                reason = fixture.UnavailableReason,
                dockerKey,
                database
            });
        });
    }
}
