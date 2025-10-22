using System;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Capabilities;

public sealed class MongoCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public MongoCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_expected_capabilities()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<MongoCapabilitiesSpec>(_output, nameof(Repository_reports_expected_capabilities))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                fixture.BindHost();

                var repo = fixture.Data.GetRepository<CapabilityProbe, string>();
                repo.Should().BeAssignableTo<ILinqQueryRepository<CapabilityProbe, string>>();
                repo.Should().BeAssignableTo<ILinqQueryRepositoryWithOptions<CapabilityProbe, string>>();

                var queryCaps = repo.Should().BeAssignableTo<IQueryCapabilities>().Subject;
                queryCaps.Capabilities.Should().Be(QueryCapabilities.Linq);

                var writeCaps = repo.Should().BeAssignableTo<IWriteCapabilities>().Subject;
                writeCaps.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
                writeCaps.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
                writeCaps.Writes.Should().HaveFlag(WriteCapabilities.AtomicBatch);
                writeCaps.Writes.Should().HaveFlag(WriteCapabilities.FastRemove);

                await CapabilityProbe.UpsertAsync(new CapabilityProbe { Name = "cap" });
                var count = await CapabilityProbe.Count.Exact();
                count.Should().Be(1);
            })
            .RunAsync();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
