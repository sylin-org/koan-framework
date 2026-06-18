using System;
using System.Linq;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Partition;

public sealed class InMemoryPartitionSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Partition_scopes_isolate_entities()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var partitionBase = NewPartition("partition");
        var partitionA = $"{partitionBase}-a";
        var partitionB = $"{partitionBase}-b";

        using (Lease(partitionA))
        {
            await TenantRecord.Upsert(new TenantRecord { Name = "A1" });
            await TenantRecord.Upsert(new TenantRecord { Name = "A2" });
        }

        using (Lease(partitionB))
        {
            await TenantRecord.Upsert(new TenantRecord { Name = "B1" });
        }

        var defaultScope = await TenantRecord.All();
        defaultScope.Should().BeEmpty();

        var partitionAResults = await TenantRecord.All(partitionA);
        partitionAResults.Should().HaveCount(2);
        partitionAResults.Select(e => e.Name).Should().BeEquivalentTo(new[] { "A1", "A2" });

        var partitionBResults = await TenantRecord.All(partitionB);
        partitionBResults.Should().HaveCount(1);
        partitionBResults[0].Name.Should().Be("B1");

        using (Lease(partitionA))
        {
            var removed = await TenantRecord.Remove(partitionAResults.Select(r => r.Id));
            removed.Should().Be(2);
        }

        var partitionAAfterDelete = await TenantRecord.All(partitionA);
        partitionAAfterDelete.Should().BeEmpty();

        var partitionBAfterDelete = await TenantRecord.All(partitionB);
        partitionBAfterDelete.Should().HaveCount(1);
    }

    private sealed class TenantRecord : Entity<TenantRecord>
    {
        public string Name { get; set; } = "";
    }
}
