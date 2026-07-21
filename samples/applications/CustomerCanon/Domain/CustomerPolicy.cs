using System.Text.RegularExpressions;

namespace CustomerCanon.Domain;

internal static partial class CustomerPolicy
{
    public const string PremiumTier = "Premium";
    public const string StandardTier = "Standard";
    public const string BasicTier = "Basic";

    private static readonly IReadOnlySet<string> PremiumCountries = new HashSet<string>(StringComparer.Ordinal)
    {
        "AU", "CA", "DE", "FR", "GB", "JP", "US"
    };

    public static void Normalize(Customer customer)
    {
        customer.Email = customer.Email.Trim().ToLowerInvariant();
        customer.FirstName = customer.FirstName.Trim();
        customer.LastName = customer.LastName.Trim();
        customer.Phone = string.IsNullOrWhiteSpace(customer.Phone)
            ? null
            : new string(customer.Phone.Where(character => character == '+' || char.IsDigit(character)).ToArray());
        customer.Country = string.IsNullOrWhiteSpace(customer.Country) ? null : customer.Country.Trim().ToUpperInvariant();
        customer.Language = string.IsNullOrWhiteSpace(customer.Language) ? null : customer.Language.Trim().ToLowerInvariant();
    }

    public static IReadOnlyList<string> Validate(Customer customer)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(customer.Email)) errors.Add("Email is required");
        if (string.IsNullOrWhiteSpace(customer.FirstName)) errors.Add("FirstName is required");
        if (string.IsNullOrWhiteSpace(customer.LastName)) errors.Add("LastName is required");

        if (!string.IsNullOrWhiteSpace(customer.Email) && !EmailRegex().IsMatch(customer.Email))
        {
            errors.Add($"Invalid email format: {customer.Email}");
        }

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            var digits = customer.Phone.Count(char.IsDigit);
            if (!customer.Phone.StartsWith('+') || digits is < 10 or > 15)
            {
                errors.Add($"Invalid phone format: {customer.Phone}");
            }
        }

        return errors;
    }

    public static void Enrich(Customer customer)
    {
        customer.DisplayName = $"{customer.FirstName} {customer.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(customer.DisplayName))
        {
            customer.DisplayName = customer.Email.Split('@')[0];
        }

        customer.AccountTier = PremiumCountries.Contains(customer.Country ?? "") && HasCompleteProfile(customer)
            ? PremiumTier
            : string.IsNullOrWhiteSpace(customer.Phone) ? BasicTier : StandardTier;
    }

    private static bool HasCompleteProfile(Customer customer) =>
        !string.IsNullOrWhiteSpace(customer.Email)
        && !string.IsNullOrWhiteSpace(customer.Phone)
        && !string.IsNullOrWhiteSpace(customer.FirstName)
        && !string.IsNullOrWhiteSpace(customer.LastName)
        && !string.IsNullOrWhiteSpace(customer.Country);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
