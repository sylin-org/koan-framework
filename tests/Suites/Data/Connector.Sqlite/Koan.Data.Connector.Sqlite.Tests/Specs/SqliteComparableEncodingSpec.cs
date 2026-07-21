using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Dockerless. Proves DateTimeOffset/TimeSpan/DateOnly/TimeOnly range
/// comparisons converge with the compiled-predicate CLR oracle through the real SQLite adapter.
/// </summary>
public sealed class SqliteComparableEncodingSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await TemporalConvergence.AssertConvergesAsync();
        await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
    }
}
