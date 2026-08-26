using Koan.Canon;

namespace Koan.Canon.Internal;

/// <summary>
/// The business checkpoint (canon-rides-jobs): first occupant of Distribution — after the synthetic
/// candidate is complete, before any distribution work. Runs the gateway's registered business
/// rules as a set; the first hold terminates the operation and parks the receipt as Refused at
/// this phase. No rules registered → no-op.
/// </summary>
internal sealed class BusinessRuleContributor<TModel> : ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Distribution;

    public int Order => int.MinValue;

    public async ValueTask<CanonizationEvent?> Execute(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
    {
        foreach (var rule in CanonEntity<TModel>.Canon.RuleSnapshot())
        {
            var justification = await rule(context.Entity).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(justification))
            {
                return context.Hold(justification);
            }
        }

        return null;
    }
}
