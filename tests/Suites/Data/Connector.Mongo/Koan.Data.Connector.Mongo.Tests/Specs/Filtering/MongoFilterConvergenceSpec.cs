using System;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Mongo derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Container-backed: skips when Docker is unavailable, otherwise runs every filter through
/// the real Mongo adapter and the in-memory floor and asserts identical id-sets. Re-validates the
/// DATA-0098 identity/enum encoding fixes end-to-end against a live store and guards the document
/// translator against future drift.
/// </summary>
public sealed class MongoFilterConvergenceSpec
{
    private readonly ITestOutputHelper _output;
    public MongoFilterConvergenceSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Mongo: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline
            .For<MongoFilterConvergenceSpec>(_output, nameof(Adapter_converges_with_oracle_across_the_corpus))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<MongoConnectorFixture>("fixture").BindHost();
                await FilterConvergence.AssertConvergesAsync();
            })
            .Run();
    }
}
