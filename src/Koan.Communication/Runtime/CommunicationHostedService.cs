using Microsoft.Extensions.Hosting;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationHostedService(InProcessTransportRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => runtime.Stop(cancellationToken);
}
