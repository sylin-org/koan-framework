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

    [Fact]
    public async Task Host_store_registry_rejects_before_exceeding_its_finite_bound()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        for (var index = 0; index < 4096; index++)
        {
            using var lease = Lease($"bounded-{index}");
            (await TenantRecord.Get("missing")).Should().BeNull();
        }

        using var overflow = Lease("bounded-overflow");
        await FluentActions.Invoking(() => TenantRecord.Get("missing"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*host bound of 4096 source/root/partition stores*");
    }

    private sealed class TenantRecord : Entity<TenantRecord>
    {
        public string Name { get; set; } = "";
    }
}
