using System;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Mongo derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Container-backed: skips without Docker. Proves that DateTimeOffset (stored
/// as a UTC BSON date) and TimeSpan (stored as Int64 ticks) — plus DateOnly/TimeOnly — produce the same
/// range-filter id-sets as pure CLR evaluation. The <c>ts-lt-1day</c> case is the regression anchor:
/// under the driver's DEFAULT TimeSpan string encoding it diverges (24h sorts before 23h); under the
/// registered Int64 encoding it converges.
/// </summary>
public sealed class MongoComparableEncodingSpec
{
    private readonly ITestOutputHelper _output;
    public MongoComparableEncodingSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Mongo: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline
            .For<MongoComparableEncodingSpec>(_output, nameof(Composite_scalars_converge_with_oracle))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<MongoConnectorFixture>("fixture").BindHost();
                await TemporalConvergence.AssertConvergesAsync();
                await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
            })
            .Run();
    }
}
