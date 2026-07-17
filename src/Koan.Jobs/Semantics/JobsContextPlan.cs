using System.ComponentModel;
using Koan.Core.Context;
using Koan.Core.Semantics.Segmentation;
using Koan.Jobs.Infrastructure;

namespace Koan.Jobs.Semantics;

/// <summary>Jobs-owned realization of hard context across durable submission and execution.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JobsContextPlan(SegmentationContextPlan context)
    : ISegmentationRealization
{
    private static readonly SegmentationRealizationDescriptor Realization = new(
        "jobs",
        "durable-context-carriage",
        [
            "chain",
            "coalesce.identity",
            "handler.execute",
            "retry",
            "submit.capture",
            "work.load",
            "work.settle"
        ]);

    public SegmentationRealizationDescriptor SegmentationRealization => Realization;

    public IReadOnlyDictionary<string, string>? Capture(Type workType)
        => context.Capture(workType, Constants.Operations.Submit);

    public IDisposable RestoreForSubmit(
        Type workType,
        IReadOnlyDictionary<string, string>? captured)
        => context.Restore(
            workType,
            captured,
            ContextIngressTrust.HostTrusted,
            Constants.Operations.Submit);

    public IDisposable RestoreForExecution(
        Type workType,
        IReadOnlyDictionary<string, string>? captured)
        => context.Restore(
            workType,
            captured,
            ContextIngressTrust.HostTrusted,
            Constants.Operations.Execute);
}
