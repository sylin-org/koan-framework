using Koan.Data.Core.Model;

namespace OrderIntake.Models;

/// <summary>
/// Tier 1: Minimal entity - just ID and timestamp.
/// Tests baseline framework and provider overhead with minimal data.
/// </summary>
public class BenchmarkMinimal : Entity<BenchmarkMinimal>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
