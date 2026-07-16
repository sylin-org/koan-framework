using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal sealed class TransportIngress(
    IServiceScopeFactory scopeFactory,
    KoanContextCarrierRegistry contextCarriers)
{
    public async Task<TransportTargetOutcome> Dispatch(
        TransportReceiverBinding receiver,
        TransportEnvelope envelope,
        CancellationToken ct)
    {
        using var serviceScope = scopeFactory.CreateScope();
        using var hostScope = AppHost.PushScope(serviceScope.ServiceProvider);
        using var contextScope = contextCarriers.Restore(
            envelope.Context,
            ContextIngressTrust.HostTrusted);
        return await receiver.Dispatch(serviceScope.ServiceProvider, envelope.Payload, ct)
            .ConfigureAwait(false);
    }
}
