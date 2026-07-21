using CustomerCanon.Domain;
using Koan.Canon;

namespace CustomerCanon.Pipeline;

internal sealed class CustomerEnrichmentContributor : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Aggregation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        CustomerPolicy.Enrich(context.Entity);
        context.Entity.UpdatedAt = DateTimeOffset.UtcNow;
        context.Metadata.State = context.Metadata.State with
        {
            Lifecycle = CanonLifecycle.Active,
            Readiness = CanonReadiness.Complete
        };
        context.Metadata.SetTag("enriched", "true");
        context.Metadata.SetTag("display_name", context.Entity.DisplayName);
        context.Metadata.SetTag("account_tier", context.Entity.AccountTier);

        return ValueTask.FromResult<CanonizationEvent?>(null);
    }
}
