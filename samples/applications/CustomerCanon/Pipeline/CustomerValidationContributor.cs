using CustomerCanon.Domain;
using Koan.Canon;

namespace CustomerCanon.Pipeline;

internal sealed class CustomerValidationContributor : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        CustomerPolicy.Normalize(context.Entity);
        var errors = CustomerPolicy.Validate(context.Entity);
        if (errors.Count == 0)
        {
            context.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            return ValueTask.FromResult<CanonizationEvent?>(null);
        }

        context.Metadata.State = context.Metadata.State with
        {
            Lifecycle = CanonLifecycle.Withdrawn,
            Readiness = CanonReadiness.Degraded
        };

        return ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
        {
            Phase = Phase,
            StageStatus = CanonStageStatus.Failed,
            CanonState = context.Metadata.State,
            OccurredAt = DateTimeOffset.UtcNow,
            Message = "Customer validation failed",
            Detail = string.Join("; ", errors)
        });
    }
}
