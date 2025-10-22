using System;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Partition;

public sealed class InMemoryPartitionSpec
{
    private readonly ITestOutputHelper _output;

    public InMemoryPartitionSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Partition_scopes_isolate_entities()
    {
        await TestPipeline.For<InMemoryPartitionSpec>(_output, nameof(Partition_scopes_isolate_entities))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<TenantRecord, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
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
