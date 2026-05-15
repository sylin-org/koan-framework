using Koan.Cache.Abstractions.Policies;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace S1.Web;

/// <summary>
/// Demonstrates Koan.Cache integration: <c>[Cacheable]</c> opts the entity into transparent
/// L1/L2 caching with a 120-second TTL. With <c>Koan.Cache</c> referenced, reads short-circuit
/// to L1 on hit; with <c>Koan.Cache.Adapter.Redis</c> added, an L2 + cross-node coherence
/// activate automatically (zero additional code).
/// </summary>
[DataAdapter("sqlite")]
[Cacheable(120)]
public sealed class Category : Entity<Category>
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}