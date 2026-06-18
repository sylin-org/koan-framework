using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// SqlServer derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Container-backed (ARCH-0091): skips without a reachable SQL Server. Proves
/// DateTimeOffset (UTC-ISO nvarchar), TimeSpan (ticks, <c>CAST ... AS BIGINT</c>), DateOnly/TimeOnly range
/// comparisons converge with the compiled-predicate CLR oracle through the real SqlServer adapter.
/// </summary>
public sealed class SqlServerComparableEncodingSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await TemporalConvergence.AssertConvergesAsync();
        await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
    }
}
