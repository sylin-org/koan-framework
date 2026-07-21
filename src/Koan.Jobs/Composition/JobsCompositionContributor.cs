using Koan.Core.Composition;
using Koan.Communication.Signals;
using Koan.Jobs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Jobs.Composition;

/// <summary>
/// Projects the host's elected Jobs ledger and Communication-backed wake path into Koan's shared composition facts.
/// It deliberately reports semantic tiers rather than CLR implementation names.
/// </summary>
internal static class JobsCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var ledger = services.GetService<IJobLedger>();
        if (ledger is not null)
        {
            var durable = ledger is RoutingJobLedger or DataJobLedger;
            builder.AddElection(
                Constants.Diagnostics.Subjects.Ledger,
                durable ? Constants.Diagnostics.Selections.DurableData : Constants.Diagnostics.Selections.InMemory,
                durable ? Constants.Diagnostics.Reasons.DurableAdapter : Constants.Diagnostics.Reasons.NoDurableAdapter,
                source: source,
                factCode: Constants.Diagnostics.Codes.LedgerSelected);
        }

        var signals = services.GetService<IFrameworkSignalPublisher>();
        if (signals is not null)
        {
            builder.AddElection(
                Constants.Diagnostics.Subjects.Wake,
                signals.ProviderId,
                Constants.Diagnostics.Reasons.CommunicationSignal,
                source: source,
                factCode: Constants.Diagnostics.Codes.WakeSelected);
        }

        var segmentation = services.GetService<SegmentationPlan>();
        if (segmentation is { IsEmpty: false })
        {
            builder.AddCapability(
                Constants.Diagnostics.Subjects.Context,
                [
                    Constants.Diagnostics.Capabilities.LogicalContext,
                    Constants.Diagnostics.Capabilities.SharedLedger,
                    Constants.Diagnostics.Capabilities.WorkItemSegmentation,
                    Constants.Diagnostics.Capabilities.AtLeastOnce,
                    Constants.Diagnostics.Capabilities.ContextFreeWake
                ]);
            builder.AddGuarantee(
                Constants.Diagnostics.Codes.ContextGuarantees,
                Constants.Diagnostics.Subjects.Context,
                "Jobs restores hard context at the host-trusted ledger boundary before work-item load and keeps it " +
                "through handler and settle. The ledger is shared control-plane state, work-item isolation is Data-owned, " +
                "execution is at-least-once, and the Communication wake is a context-free latency hint.",
                Constants.Diagnostics.Reasons.DurableContext,
                source);
        }
    }
}
