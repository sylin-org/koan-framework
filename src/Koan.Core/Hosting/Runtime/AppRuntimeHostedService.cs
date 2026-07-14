using Microsoft.Extensions.Hosting;

namespace Koan.Core.Hosting.Runtime;

/// <summary>Runs runtime discovery for every generic host; AppRuntime keeps duplicate hosting calls idempotent.</summary>
internal sealed class AppRuntimeHostedService(IAppRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runtime.Discover();
        runtime.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
