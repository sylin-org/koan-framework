using Koan.Testing;
using S1.Web;
using Xunit;

// Conformance specs share the process-global ambient host — run them sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace S1.Web.Tests;

/// <summary>
/// P2.1 dogfood — the S1.Web <see cref="User"/> entity inherits a test suite. One method; the batteries
/// (round-trip, pushdown-vs-oracle, paging, partition isolation) run against User's real sqlite adapter,
/// no Docker required. The trait-gated cache/embedding batteries self-skip (User declares neither).
/// </summary>
public sealed class UserConformance : EntityConformanceSpecs<User>
{
    protected override User NewValid() => new() { Name = "Ada Lovelace", Email = "ada@example.com" };
}
