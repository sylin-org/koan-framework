using System.Text.RegularExpressions;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using S8.Canon.Domain;

namespace S8.Canon.Pipeline;

/// <summary>
/// Validates and normalizes customer data during the Validation phase.
/// Ensures required fields are present, formats are valid, and data is normalized.
/// </summary>
public partial class CustomerValidationContributor : ICanonPipelineContributor<Customer>
{
    /// <inheritdoc />
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    /// <inheritdoc />
    public async ValueTask<CanonizationEvent?> ExecuteAsync(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        var customer = context.Entity;
        var errors = new List<string>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(customer.Email))
        {
            errors.Add("Email is required");
        }

        if (string.IsNullOrWhiteSpace(customer.FirstName))
        {
            errors.Add("FirstName is required");
        }

        if (string.IsNullOrWhiteSpace(customer.LastName))
        {
            errors.Add("LastName is required");
        }

        // Validate email format
        if (!string.IsNullOrWhiteSpace(customer.Email) && !IsValidEmail(customer.Email))
        {
            errors.Add($"Invalid email format: {customer.Email}");
        }

        // Validate phone format (if provided)
        if (!string.IsNullOrWhiteSpace(customer.Phone) && !IsValidPhone(customer.Phone))
        {
            errors.Add($"Invalid phone format: {customer.Phone}");
        }

        // Return error event if validation failed
        if (errors.Count > 0)
        {
            // Set state via metadata (entity.State has private setter)
            context.Metadata.State = context.Metadata.State with
            {
                Lifecycle = CanonLifecycle.Withdrawn,
                Readiness = CanonReadiness.Degraded
            };

            return new CanonizationEvent
            {
                Phase = CanonPipelinePhase.Validation,
                StageStatus = CanonStageStatus.Failed,
                CanonState = context.Metadata.State,
                OccurredAt = DateTimeOffset.UtcNow,
                Message = "Customer validation failed",
                Detail = string.Join("; ", errors)
            };
        }

        // Normalize data
        customer.Email = customer.Email.Trim().ToLowerInvariant();
        customer.FirstName = customer.FirstName.Trim();
        customer.LastName = customer.LastName.Trim();

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            customer.Phone = NormalizePhone(customer.Phone);
        }

        if (!string.IsNullOrWhiteSpace(customer.Country))
        {
            customer.Country = customer.Country.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(customer.Language))
        {
            customer.Language = customer.Language.Trim().ToLowerInvariant();
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;

        // Validation passed - return null (default success event will be generated)
        return await ValueTask.FromResult<CanonizationEvent?>(null);
    }

    private static bool IsValidEmail(string email)
    {
        return EmailRegex().IsMatch(email);
    }

    private static bool IsValidPhone(string phone)
    {
        // Simple check: starts with + and has 10-15 digits
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone.StartsWith('+') && digits.Length >= 10 && digits.Length <= 15;
    }

    private static string NormalizePhone(string phone)
    {
        // Keep only + and digits
        var normalized = new string(phone.Where(c => c == '+' || char.IsDigit(c)).ToArray());
        return normalized;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
