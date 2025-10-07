using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelinePostgresExtensions
{
    public static TestPipeline UsingPostgresContainer(this TestPipeline pipeline, string key = "postgres", string dockerKey = "docker", string database = "Koan")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<PostgresContainerFixture>(key, _ => ValueTask.FromResult(new PostgresContainerFixture(dockerKey, database)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("postgres.fixture.ready", new
            {
                available = fixture.IsAvailable,
                connectionString = fixture.ConnectionString,
                reason = fixture.UnavailableReason,
                dockerKey,
                database = fixture.Database
            });
        });
    }
}
