using Koan.Core.Composition;
using Koan.Communication.Signals;
using Koan.Jobs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Composition;

/// <summary>
/// Projects the host's elected Jobs ledger and Communication-backed wake path into Koan's shared composition facts.
/// It deliberately reports semantic tiers rather than CLR implementation names.
/// </summary>
internal sealed class JobsCompositionContributor : IKoanCompositionContributor
{
    public void Contribute(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var ledger = services.GetService<IJobLedger>();
        if (ledger is not null)
        {
            var durable = ledger is RoutingJobLedger or DataJobLedger;
            builder.AddElection(
                Constants.Diagnostics.Subjects.Ledger,
                durable ? Constants.Diagnostics.Selections.DurableData : Constants.Diagnostics.Selections.InMemory,
                durable ? Constants.Diagnostics.Reasons.DurableAdapter : Constants.Diagnostics.Reasons.NoDurableAdapter,
                source: typeof(JobsCompositionContributor).FullName,
                factCode: Constants.Diagnostics.Codes.LedgerSelected);
        }

        var signals = services.GetService<IFrameworkSignalPublisher>();
        if (signals is not null)
        {
            builder.AddElection(
                Constants.Diagnostics.Subjects.Wake,
                signals.ProviderId,
                Constants.Diagnostics.Reasons.CommunicationSignal,
                source: typeof(JobsCompositionContributor).FullName,
                factCode: Constants.Diagnostics.Codes.WakeSelected);
        }
    }
}
