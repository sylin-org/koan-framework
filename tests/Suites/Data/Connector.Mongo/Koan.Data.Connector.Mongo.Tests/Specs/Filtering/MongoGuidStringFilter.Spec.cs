using System;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Regression for DATA-XXXX (the read↔write GUID-encoding asymmetry). The global
/// SmartStringGuidSerializer persists any Guid-parseable STRING as UUID BinData; the query
/// translator must encode the comparison value the same way (the shared MongoGuidEncoding rule) or a
/// predicate on a Guid-shaped string field never matches its stored value — returning empty rows
/// moments after they were written, which silently triggered delete-when-empty data loss.
/// </summary>
public sealed class MongoGuidStringFilterSpec
{
    private readonly ITestOutputHelper _output;

    public MongoGuidStringFilterSpec(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Query_by_guid_shaped_string_fk_finds_rows_written_moments_earlier()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<MongoGuidStringFilterSpec>(_output, nameof(Query_by_guid_shaped_string_fk_finds_rows_written_moments_earlier))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                await fixture.ResetAsync<Sighting, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                var packageId = Guid.NewGuid().ToString();   // a Guid-shaped string FK (stored as BinData)
                var otherId = Guid.NewGuid().ToString();

                await Sighting.UpsertMany(new[]
                {
                    new Sighting { PackageId = packageId, Label = "a" },
                    new Sighting { PackageId = packageId, Label = "b" },
                    new Sighting { PackageId = otherId,   Label = "c" }
                });

                // The failing path: read-after-write on a Guid-shaped string FK. Pre-fix this was 0.
                var active = await Data<Sighting, string>.Query(s => s.PackageId == packageId, partition);
                active.Should().HaveCount(2);
                active.Select(s => s.Label).Should().BeEquivalentTo(new[] { "a", "b" });

                // A package with no sightings must still return empty (so delete-when-empty stays correct
                // when there genuinely are none — the bug made EVERY package look empty).
                var none = await Data<Sighting, string>.Query(s => s.PackageId == Guid.NewGuid().ToString(), partition);
                none.Should().BeEmpty();

                // Membership (In) over Guid-shaped strings must also match.
                var ids = new[] { packageId, otherId };
                var both = await Data<Sighting, string>.Query(s => ids.Contains(s.PackageId), partition);
                both.Should().HaveCount(3);
            })
            .Run();
    }

    private sealed class Sighting : Entity<Sighting>
    {
        public string PackageId { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
