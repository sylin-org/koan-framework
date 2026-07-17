using Koan.Canon;

namespace CustomerCanon.Domain;

public class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";

    public string? Phone { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccountTier { get; set; } = CustomerPolicy.BasicTier;
    public string? Country { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
