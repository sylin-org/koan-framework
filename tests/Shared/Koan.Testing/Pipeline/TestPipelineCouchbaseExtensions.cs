using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineCouchbaseExtensions
{
    public static TestPipeline UsingCouchbaseContainer(this TestPipeline pipeline, string key = "couchbase", string dockerKey = "docker", string bucket = "koan")
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<CouchbaseContainerFixture>(key, _ => ValueTask.FromResult(new CouchbaseContainerFixture(dockerKey, bucket)), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("couchbase.fixture.ready", new
            {
                available = fixture.IsAvailable,
                connectionString = fixture.ConnectionString,
                bucket = fixture.Bucket,
                reason = fixture.UnavailableReason,
                dockerKey
            });
        });
    }
}
