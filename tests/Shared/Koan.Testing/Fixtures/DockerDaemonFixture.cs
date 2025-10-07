using Docker.DotNet;
using Koan.Testing.Contracts;
using Koan.Testing.Infrastructure;

namespace Koan.Testing.Fixtures;

public sealed class DockerDaemonFixture : IAsyncDisposable, IInitializableFixture
{
    private const string RyukDisabledVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private DockerEnvironment.ProbeResult? _probe;

    public DockerEnvironment.ProbeResult? Probe => _probe;

    public bool IsAvailable => _probe?.Available ?? false;

    public string? Endpoint => _probe?.Endpoint;

    public string? Message => _probe?.Message;

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Diagnostics.Info("docker.probe.begin");
        _probe = await DockerEnvironment.ProbeAsync().ConfigureAwait(false);
        context.Diagnostics.Info("docker.probe.end", new
        {
            available = _probe.Available,
            endpoint = _probe.Endpoint,
            message = _probe.Message
        });

        if (IsAvailable)
        {
            Environment.SetEnvironmentVariable(RyukDisabledVariable, "true");
            context.Diagnostics.Debug("docker.ryuk.disabled", new { variable = RyukDisabledVariable });
        }
    }

    public DockerClient CreateClient()
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(Endpoint))
        {
            var reason = Message ?? "Docker daemon is not reachable";
            throw new InvalidOperationException($"Docker unavailable: {reason}");
        }

        return DockerEnvironment.CreateClient(Endpoint);
    }

    public string UnavailableReason => Message ?? "Docker daemon is not reachable";

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
