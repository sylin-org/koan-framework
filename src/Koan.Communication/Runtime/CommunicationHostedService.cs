using Microsoft.Extensions.Hosting;

using Koan.Communication.Signals;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationHostedService(
    CommunicationRouter router,
    IFrameworkSignalPublisher frameworkSignals) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await router.Start(cancellationToken).ConfigureAwait(false);
        try
        {
            await frameworkSignals.Start(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await router.Stop(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await frameworkSignals.Stop(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await router.Stop(cancellationToken).ConfigureAwait(false);
        }
    }
}
