using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using S8.Canon.Domain;

namespace S8.Canon.Pipeline;

/// <summary>
/// Enriches customer data during the Aggregation phase.
/// Computes derived fields like DisplayName and AccountTier based on customer attributes.
/// </summary>
public class CustomerEnrichmentContributor : ICanonPipelineContributor<Customer>
{
    /// <inheritdoc />
    public CanonPipelinePhase Phase => CanonPipelinePhase.Aggregation;

    /// <inheritdoc />
    public async ValueTask<CanonizationEvent?> ExecuteAsync(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        var customer = context.Entity;

        // Compute display name
        customer.DisplayName = ComputeDisplayName(customer);

        // Compute account tier based on business rules
        customer.AccountTier = ComputeAccountTier(customer);

        // Mark as active and complete
        context.Metadata.State = context.Metadata.State with
        {
            Lifecycle = CanonLifecycle.Active,
            Readiness = CanonReadiness.Complete
        };

        customer.UpdatedAt = DateTimeOffset.UtcNow;

        // Add enrichment metadata
        context.Metadata.SetTag("enriched", "true");
        context.Metadata.SetTag("display_name", customer.DisplayName);
        context.Metadata.SetTag("account_tier", customer.AccountTier);

        // Return null for default success event
        return await ValueTask.FromResult<CanonizationEvent?>(null);
    }

    private static string ComputeDisplayName(Customer customer)
    {
        // Try full name
        if (!string.IsNullOrWhiteSpace(customer.FirstName) && !string.IsNullOrWhiteSpace(customer.LastName))
        {
            return $"{customer.FirstName} {customer.LastName}";
        }

        // Fallback to email prefix
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            var emailPrefix = customer.Email.Split('@')[0];
            return emailPrefix;
        }

        return "Unknown Customer";
    }

    private static string ComputeAccountTier(Customer customer)
    {
        // Premium: Customers from premium countries with complete profiles
        if (IsPremiumCountry(customer.Country) && HasCompleteProfile(customer))
        {
            return "Premium";
        }

        // Standard: Customers with phone number
        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            return "Standard";
        }

        // Basic: Everyone else
        return "Basic";
    }

    private static bool IsPremiumCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return false;
        }

        // Premium countries (example business logic)
        var premiumCountries = new HashSet<string> { "US", "GB", "DE", "FR", "JP", "AU", "CA" };
        return premiumCountries.Contains(country.ToUpperInvariant());
    }

    private static bool HasCompleteProfile(Customer customer)
    {
        return !string.IsNullOrWhiteSpace(customer.Email)
            && !string.IsNullOrWhiteSpace(customer.Phone)
            && !string.IsNullOrWhiteSpace(customer.FirstName)
            && !string.IsNullOrWhiteSpace(customer.LastName)
            && !string.IsNullOrWhiteSpace(customer.Country);
    }
}
