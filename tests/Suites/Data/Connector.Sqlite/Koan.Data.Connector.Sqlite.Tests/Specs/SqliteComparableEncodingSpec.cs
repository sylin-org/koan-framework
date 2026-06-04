using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Sqlite.Tests.Support;
using Koan.Testing.Contracts;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Dockerless. Proves DateTimeOffset/TimeSpan/DateOnly/TimeOnly range
/// comparisons converge with the compiled-predicate CLR oracle through the real SQLite adapter.
/// </summary>
public sealed class SqliteComparableEncodingSpec
{
    private readonly ITestOutputHelper _output;
    public SqliteComparableEncodingSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Sqlite: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        await TestPipeline
            .For<SqliteComparableEncodingSpec>(_output, nameof(Composite_scalars_converge_with_oracle))
            .Using<SqliteConnectorFixture>("fixture", static ctx => SqliteConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<SqliteConnectorFixture>("fixture").BindHost();
                await TemporalConvergence.AssertConvergesAsync();
                await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
            })
            .Run();
    }
}
