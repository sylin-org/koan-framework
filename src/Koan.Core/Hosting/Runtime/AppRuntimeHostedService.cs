using Microsoft.Extensions.Hosting;

namespace Koan.Core.Hosting.Runtime;

/// <summary>
/// Runs runtime discovery for every generic host; AppRuntime keeps duplicate hosting calls idempotent. Discovery runs in
/// <see cref="StartingAsync"/>, after the host is attached and before any hosted service's <c>StartAsync</c>, whatever
/// order the application registered its hosted services in (ARCH-0140).
/// </summary>
internal sealed class AppRuntimeHostedService(IAppRuntime runtime) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runtime.Discover();
        runtime.Start();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
