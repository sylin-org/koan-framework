using System;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Postgres.Tests.Support;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>
/// Postgres derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Container-backed: skips when Docker is unavailable, otherwise runs every filter through
/// the real Postgres adapter and the in-memory floor and asserts identical id-sets. This verifies
/// whether Postgres's jsonb collection-containment pushdown converges (the SQLite sibling needed the
/// json_each column qualified — this is how we learn if Postgres shares the class).
/// </summary>
public sealed class PostgresFilterConvergenceSpec
{
    private readonly ITestOutputHelper _output;
    public PostgresFilterConvergenceSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Postgres: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline
            .For<PostgresFilterConvergenceSpec>(_output, nameof(Adapter_converges_with_oracle_across_the_corpus))
            .RequireDocker()
            .UsingPostgresContainer(database: databaseName)
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<PostgresConnectorFixture>("fixture").BindHost();
                await FilterConvergence.AssertConvergesAsync();
            })
            .Run();
    }
}
