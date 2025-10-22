using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineDockerExtensions
{
    public static TestPipeline UsingDocker(this TestPipeline pipeline, string key = "docker", Action<TestContext, DockerDaemonFixture>? onUnavailable = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return pipeline.Using<DockerDaemonFixture>(key, _ => ValueTask.FromResult(new DockerDaemonFixture()), (ctx, fixture) =>
        {
            ctx.Diagnostics.Info("docker.fixture.ready", new
            {
                available = fixture.IsAvailable,
                endpoint = fixture.Endpoint,
                message = fixture.Message
            });

            if (!fixture.IsAvailable)
            {
                ctx.Diagnostics.Warn("docker.unavailable", new { reason = fixture.UnavailableReason });
                onUnavailable?.Invoke(ctx, fixture);
            }
        });
    }

    public static TestPipeline RequireDocker(this TestPipeline pipeline, string key = "docker")
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return pipeline.UsingDocker(key, (_, fixture) => throw new InvalidOperationException($"Docker unavailable: {fixture.UnavailableReason}"));
    }
}
