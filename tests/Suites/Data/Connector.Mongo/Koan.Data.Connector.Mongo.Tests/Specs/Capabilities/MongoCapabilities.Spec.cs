using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
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
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.Create(ctx))
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
                repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

                // ARCH-0084: negotiate via the unified CapabilitySet.
                var caps = DataCaps.Describe(repo, repo.GetType().Name);
                caps.Has(DataCaps.Query.Linq).Should().BeTrue();
                caps.Has(DataCaps.Query.String).Should().BeFalse();
                caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
                caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
                caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();
                caps.Has(DataCaps.Write.FastRemove).Should().BeTrue();

                await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
                var count = await CapabilityProbe.Count.Exact();
                count.Should().Be(1);
            })
            .Run();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
