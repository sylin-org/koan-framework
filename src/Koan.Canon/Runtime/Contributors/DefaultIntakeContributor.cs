using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon;

namespace Koan.Canon.Internal;

/// <summary>
/// Runs the model's <see cref="CanonEntity{TModel}.OnIntake"/> override first in the Validation
/// phase: arrival normalization happens before user validators and before aggregation keys are
/// matched, so identity tokens always see prepared values.
/// </summary>
internal sealed class DefaultIntakeContributor<TModel> : ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // The model's own override speaks first, then composition-registered rules on the type gateway.
        var onboarded = context.Entity.OnIntake(context.Entity)
            ?? throw new InvalidOperationException(
                $"Canonical entity '{typeof(TModel).Name}' returned null from {nameof(CanonEntity<TModel>.OnIntake)}. " +
                "Return the candidate (transformed or not); rejection belongs to a Validation contributor emitting a Failed event.");

        if (!ReferenceEquals(onboarded, context.Entity))
        {
            throw new InvalidOperationException(
                $"Canonical entity '{typeof(TModel).Name}' returned a different instance from {nameof(CanonEntity<TModel>.OnIntake)}. " +
                "Mutate the candidate in place and return it - the pipeline carries one arriving instance through its phases.");
        }

        onboarded = CanonEntity<TModel>.Canon.ApplyIntakeRules(onboarded);

        return ValueTask.FromResult<CanonizationEvent?>(null);
    }
}
