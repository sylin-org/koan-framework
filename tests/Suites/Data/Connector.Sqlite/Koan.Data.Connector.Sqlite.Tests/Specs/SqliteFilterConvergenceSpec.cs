using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Sqlite.Tests.Support;
using Koan.Testing.Contracts;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Dockerless — runs on every build. Every filter is run through the real SQLite adapter
/// and the in-memory floor; identical id-sets are asserted. This is the spec that caught the
/// correlated-json_each collection-pushdown bug.
/// </summary>
public sealed class SqliteFilterConvergenceSpec
{
    private readonly ITestOutputHelper _output;
    public SqliteFilterConvergenceSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Sqlite: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        await TestPipeline
            .For<SqliteFilterConvergenceSpec>(_output, nameof(Adapter_converges_with_oracle_across_the_corpus))
            .Using<SqliteConnectorFixture>("fixture", static ctx => SqliteConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<SqliteConnectorFixture>("fixture").BindHost();
                await FilterConvergence.AssertConvergesAsync();
            })
            .Run();
    }
}
