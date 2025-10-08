using System;
using Koan.Data.Abstractions;
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
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.CreateAsync(ctx))
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
                repo.Should().BeAssignableTo<ILinqQueryRepository<CapabilityProbe, string>>();
                repo.Should().BeAssignableTo<ILinqQueryRepositoryWithOptions<CapabilityProbe, string>>();
                repo.Should().BeAssignableTo<IStringQueryRepository<CapabilityProbe, string>>();
                repo.Should().BeAssignableTo<IStringQueryRepositoryWithOptions<CapabilityProbe, string>>();

                var queryCaps = repo.Should().BeAssignableTo<IQueryCapabilities>().Subject;
                queryCaps.Capabilities.Should().Be(QueryCapabilities.Linq | QueryCapabilities.String);

                var writeCaps = repo.Should().BeAssignableTo<IWriteCapabilities>().Subject;
                writeCaps.Writes.Should().Be(WriteCapabilities.AtomicBatch | WriteCapabilities.BulkDelete | WriteCapabilities.FastRemove);

                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await CapabilityProbe.UpsertAsync(new CapabilityProbe { Name = "cap" });
                var count = await CapabilityProbe.Count.Exact();
                count.Should().Be(1);

                var linqQuery = await CapabilityProbe.Query(p => p.Name == "cap");
                linqQuery.Should().ContainSingle();
            })
            .RunAsync();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
