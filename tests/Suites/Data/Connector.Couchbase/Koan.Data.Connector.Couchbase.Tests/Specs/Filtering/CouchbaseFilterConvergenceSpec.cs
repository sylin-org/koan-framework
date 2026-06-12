using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Couchbase.Tests.Support;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Filtering;

/// <summary>
/// Couchbase derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Couchbase is the only remaining data connector that PUSHES filters down — it translates
/// the <c>Filter</c> AST to N1QL (<c>CouchbaseN1qlFilterTranslator</c>) and runs it server-side — so it is
/// the one place a translation bug can hide (enum encoding, null semantics, array containment). The Json /
/// Redis / InMemory connectors filter client-side through the same in-memory evaluator the oracle uses, so
/// a convergence test there would be tautological.
///
/// Runs every filter in the corpus through the real Couchbase cluster and the in-memory floor and asserts
/// identical id-sets. Skips (RequireDocker) when no Docker/Couchbase is reachable.
/// </summary>
public sealed class CouchbaseFilterConvergenceSpec
{
    private readonly ITestOutputHelper _output;

    public CouchbaseFilterConvergenceSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Couchbase: every filter converges with the in-memory oracle")]
    [Trait("Category", "Integration")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        await TestPipeline.For<CouchbaseFilterConvergenceSpec>(_output, nameof(Adapter_converges_with_oracle_across_the_corpus))
            .RequireDocker()
            .UsingCouchbaseContainer()
            .Using<CouchbaseConnectorFixture>("fixture", static ctx => CouchbaseConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<CouchbaseConnectorFixture>("fixture");
                fixture.BindHost();

                // ConvergenceWidget is a partition-agnostic entity; Couchbase scopes by the ambient
                // partition, so establish a lease before clearing/seeding the corpus.
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await FilterConvergence.AssertConvergesAsync();
            })
            .Run();
    }
}
