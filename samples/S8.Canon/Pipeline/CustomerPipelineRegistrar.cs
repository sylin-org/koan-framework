using Koan.Canon.Domain.Runtime;
using S8.Canon.Domain;

namespace S8.Canon.Pipeline;

/// <summary>
/// Configures the Customer canonization pipeline with validation and enrichment contributors.
/// Registered automatically via KoanAutoRegistrar.
/// </summary>
public class CustomerPipelineRegistrar : ICanonRuntimeConfigurator
{
    /// <inheritdoc />
    public void Configure(CanonRuntimeBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Configure Customer pipeline with contributors
        builder.ConfigurePipeline<Customer>(pipeline =>
        {
            pipeline.AddContributor(new CustomerValidationContributor());
            pipeline.AddContributor(new CustomerEnrichmentContributor());
        });
    }
}
