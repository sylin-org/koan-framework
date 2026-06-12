using System;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Postgres.Tests.Support;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>
/// Postgres derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Container-backed: skips without Docker. Proves DateTimeOffset (UTC-ISO text),
/// TimeSpan (ticks, <c>::numeric</c> cast), DateOnly/TimeOnly range comparisons converge with the
/// compiled-predicate CLR oracle through the real Postgres adapter — including mixed-offset DateTimeOffset
/// and across-the-day-boundary TimeSpan, the cases that diverge under the default JSON-text encoding.
/// </summary>
public sealed class PostgresComparableEncodingSpec
{
    private readonly ITestOutputHelper _output;
    public PostgresComparableEncodingSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Postgres: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline
            .For<PostgresComparableEncodingSpec>(_output, nameof(Composite_scalars_converge_with_oracle))
            .RequireDocker()
            .UsingPostgresContainer(database: databaseName)
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<PostgresConnectorFixture>("fixture").BindHost();
                await TemporalConvergence.AssertConvergesAsync();
                await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
            })
            .Run();
    }
}
