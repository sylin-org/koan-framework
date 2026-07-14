using Koan.Core.Composition;
using Koan.Jobs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Composition;

/// <summary>
/// Projects the host's elected Jobs ledger and wake transport into Koan's shared composition facts.
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

        var transport = services.GetService<IJobTransport>();
        if (transport is not null)
        {
            var builtIn = transport is InProcessJobTransport;
            builder.AddElection(
                Constants.Diagnostics.Subjects.Transport,
                builtIn ? Constants.Diagnostics.Selections.InProcess : Constants.Diagnostics.Selections.Custom,
                builtIn ? Constants.Diagnostics.Reasons.DefaultTransport : Constants.Diagnostics.Reasons.RegisteredTransport,
                source: typeof(JobsCompositionContributor).FullName,
                factCode: Constants.Diagnostics.Codes.TransportSelected);
        }
    }
}
