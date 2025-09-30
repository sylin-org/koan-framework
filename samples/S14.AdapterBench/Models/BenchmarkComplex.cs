using Koan.Data.Core.Model;

namespace S14.AdapterBench.Models;

/// <summary>
/// Tier 3: Complex document-style entity with nested objects.
/// Tests large payload handling and nested data serialization.
/// </summary>
public class BenchmarkComplex : Entity<BenchmarkComplex>
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public Address Address { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "US";
}
