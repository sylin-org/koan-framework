using Microsoft.Extensions.Hosting;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationHostedService(CommunicationRouter router) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => router.Start(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => router.Stop(cancellationToken);
}
