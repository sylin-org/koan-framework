using System.ComponentModel;
using Koan.Communication.Adapters;
using Koan.Communication.Infrastructure;
using Koan.Core.Context;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Communication.Semantics;

/// <summary>Communication-owned realization of hard context across publication and typed ingress.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class CommunicationContextPlan(SegmentationContextPlan context)
    : ISegmentationRealization
{
    private static readonly SegmentationRealizationDescriptor Realization = new(
        "communication",
        "typed-context-carriage",
        ["events.capture", "events.ingress", "transport.capture", "transport.ingress"]);

    public SegmentationRealizationDescriptor SegmentationRealization => Realization;

    public ContextIngressTrust MinimumIngressTrust => context.MinimumIngressTrust;

    public IReadOnlyDictionary<string, string>? Capture(Type subject, CommunicationLane lane)
        => context.Capture(subject, lane switch
        {
            CommunicationLane.Transport => Constants.Operations.Send,
            CommunicationLane.Events => Constants.Operations.Raise,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
        });

    public IDisposable Restore(
        Type subject,
        IReadOnlyDictionary<string, string>? captured,
        CommunicationLane lane,
        ContextIngressTrust ingressTrust)
        => context.Restore(subject, captured, ingressTrust, Operation(lane));

    private static string Operation(CommunicationLane lane)
        => lane switch
        {
            CommunicationLane.Transport => Constants.Operations.Receive,
            CommunicationLane.Events => Constants.Operations.Handle,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
        };
}
