using System.IO;
using System.Linq;

namespace Koan.Data.Connector.Json.Tests.Specs.Partition;

public sealed class JsonPartitionSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Partition_scopes_isolate_entities()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var partitionRoot = NewPartition("partition");
        var partitionA = $"{partitionRoot}-a";
        var partitionB = $"{partitionRoot}-b";

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

        var afterADelete = await TenantRecord.All(partitionA);
        afterADelete.Should().BeEmpty();

        var afterBDelete = await TenantRecord.All(partitionB);
        afterBDelete.Should().ContainSingle();

        var jsonFiles = Directory.Exists(Fixture.RootPath)
            ? Directory.EnumerateFiles(Fixture.RootPath, "*.json", SearchOption.AllDirectories).ToArray()
            : [];

        jsonFiles.Should().NotBeEmpty("each partition should materialize its own json store");
    }

    private sealed class TenantRecord : Entity<TenantRecord>
    {
        public string Name { get; set; } = "";
    }
}
