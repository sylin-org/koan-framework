using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextDockerExtensions
{
    public static DockerDaemonFixture GetDockerFixture(this TestContext context, string key = "docker")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<DockerDaemonFixture>(key);
    }

    public static bool IsDockerAvailable(this TestContext context, string key = "docker")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetDockerFixture(key).IsAvailable;
    }

    public static string GetDockerUnavailableReason(this TestContext context, string key = "docker")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetDockerFixture(key).UnavailableReason;
    }
}
