using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Koan.Communication.Semantics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationIngress(
    IServiceScopeFactory scopeFactory,
    KoanContextCarrierRegistry contextCarriers,
    CommunicationContextPlan contextPlan)
{
    public async Task<CommunicationTargetOutcome> Dispatch(
        CommunicationTargetBinding target,
        CommunicationEnvelope envelope,
        ContextIngressTrust ingressTrust,
        CancellationToken ct)
    {
        using var serviceScope = scopeFactory.CreateScope();
        using var hostScope = AppHost.PushScope(serviceScope.ServiceProvider);
        if (envelope.Lane is Adapters.CommunicationLane.Transport or Adapters.CommunicationLane.Events)
        {
            using var contextScope = contextPlan.Restore(
                envelope.ContractType,
                envelope.Context,
                envelope.Lane,
                ingressTrust);
            return await target.Dispatch(serviceScope.ServiceProvider, envelope, ct).ConfigureAwait(false);
        }

        using var frameworkContextScope = contextCarriers.Restore(envelope.Context, ingressTrust);
        return await target.Dispatch(serviceScope.ServiceProvider, envelope, ct).ConfigureAwait(false);
    }
}
