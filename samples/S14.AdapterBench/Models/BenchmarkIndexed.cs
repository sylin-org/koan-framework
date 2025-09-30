using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S14.AdapterBench.Models;

/// <summary>
/// Tier 2: Indexed business entity - typical CRUD scenario.
/// Tests indexed query performance and business logic overhead.
/// </summary>
public class BenchmarkIndexed : Entity<BenchmarkIndexed>
{
    [Index]
    public string UserId { get; set; } = "";

    [Index]
    public string Category { get; set; } = "";

    [Index]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Title { get; set; } = "";

    public decimal Amount { get; set; }

    public string Status { get; set; } = "Active";
}
