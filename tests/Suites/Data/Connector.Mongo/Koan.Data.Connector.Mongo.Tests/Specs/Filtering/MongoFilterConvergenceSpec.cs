using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Mongo derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Container-backed: skips when Docker is unavailable, otherwise runs every filter through
/// the real Mongo adapter and the in-memory floor and asserts identical id-sets. Re-validates the
/// DATA-0098 identity/enum encoding fixes end-to-end against a live store and guards the document
/// translator against future drift.
/// </summary>
public sealed class MongoFilterConvergenceSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }
}
