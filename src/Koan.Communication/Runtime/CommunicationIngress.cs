using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationIngress(
    IServiceScopeFactory scopeFactory,
    KoanContextCarrierRegistry contextCarriers)
{
    public async Task<CommunicationTargetOutcome> Dispatch(
        CommunicationTargetBinding target,
        CommunicationEnvelope envelope,
        CancellationToken ct)
    {
        using var serviceScope = scopeFactory.CreateScope();
        using var hostScope = AppHost.PushScope(serviceScope.ServiceProvider);
        using var contextScope = contextCarriers.Restore(
            envelope.Context,
            ContextIngressTrust.HostTrusted);
        return await target.Dispatch(serviceScope.ServiceProvider, envelope, ct)
            .ConfigureAwait(false);
    }
}
