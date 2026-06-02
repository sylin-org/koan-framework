using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Connector.Postgres.Tests.Support;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Capabilities;

public sealed class PostgresCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public PostgresCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_expected_capabilities()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<PostgresCapabilitiesSpec>(_output, nameof(Repository_reports_expected_capabilities))
            .RequireDocker()
            .UsingPostgresContainer(database: databaseName)
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
                fixture.BindHost();

                var repo = fixture.Data.GetRepository<CapabilityProbe, string>();
                repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();
                repo.Should().BeAssignableTo<IRawQueryRepository<CapabilityProbe, string>>();

                // ARCH-0084: negotiate via the unified CapabilitySet.
                var caps = DataCaps.Describe(repo, repo.GetType().Name);
                caps.Has(DataCaps.Query.Linq).Should().BeTrue();
                caps.Has(DataCaps.Query.String).Should().BeTrue();
                caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();
                caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
                caps.Has(DataCaps.Write.FastRemove).Should().BeTrue();
                caps.Has(DataCaps.Write.BulkUpsert).Should().BeFalse();

                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
                var count = await CapabilityProbe.Count.Exact();
                count.Should().Be(1);

                var linqQuery = await CapabilityProbe.Query(p => p.Name == "cap");
                linqQuery.Should().ContainSingle();
            })
            .Run();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
