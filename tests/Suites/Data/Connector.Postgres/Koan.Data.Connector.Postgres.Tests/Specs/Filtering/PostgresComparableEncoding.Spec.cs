using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>
/// Postgres derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Container-backed (ARCH-0091): skips without Docker. Proves DateTimeOffset,
/// TimeSpan, DateOnly/TimeOnly range comparisons converge with the compiled-predicate CLR oracle
/// through the real Postgres adapter — including mixed-offset DateTimeOffset and across-the-day-boundary
/// TimeSpan, the cases that diverge under the default JSON-text encoding.
/// </summary>
public sealed class PostgresComparableEncodingSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await TemporalConvergence.AssertConvergesAsync();
        await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
    }
}
