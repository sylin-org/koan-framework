using System;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Partition;

public sealed class MongoPartitionSpec
{
    private readonly ITestOutputHelper _output;

    public MongoPartitionSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Partition_scopes_isolate_entities()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<MongoPartitionSpec>(_output, nameof(Partition_scopes_isolate_entities))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                await fixture.ResetAsync<TenantRecord, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                fixture.BindHost();

                var partitionBase = fixture.EnsurePartition(ctx);
                var partitionA = $"{partitionBase}-a";
                var partitionB = $"{partitionBase}-b";

                await using (fixture.LeasePartition(partitionA))
                {
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "A1" });
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "A2" });
                }

                await using (fixture.LeasePartition(partitionB))
                {
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "B1" });
                }

                var defaultScope = await TenantRecord.All();
                defaultScope.Should().BeEmpty();

                var partitionAResults = await TenantRecord.All(partitionA);
                partitionAResults.Should().HaveCount(2);
                partitionAResults.Select(e => e.Name).Should().BeEquivalentTo(new[] { "A1", "A2" });

                var partitionBResults = await TenantRecord.All(partitionB);
                partitionBResults.Should().HaveCount(1);
                partitionBResults[0].Name.Should().Be("B1");

                await using (fixture.LeasePartition(partitionA))
                {
                    var removed = await TenantRecord.Remove(partitionAResults.Select(r => r.Id));
                    removed.Should().Be(2);
                }

                var partitionAAfterDelete = await TenantRecord.All(partitionA);
                partitionAAfterDelete.Should().BeEmpty();

                var partitionBAfterDelete = await TenantRecord.All(partitionB);
                partitionBAfterDelete.Should().HaveCount(1);
            })
            .RunAsync();
    }

    private sealed class TenantRecord : Entity<TenantRecord>
    {
        public string Name { get; set; } = string.Empty;
    }
}
