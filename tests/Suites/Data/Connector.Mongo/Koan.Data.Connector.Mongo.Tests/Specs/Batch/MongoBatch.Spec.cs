namespace Koan.Data.Connector.Mongo.Tests.Specs.Batch;

public sealed class MongoBatchSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Batch_operations_apply_atomically()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("batch");
        using var lease = Lease(partition);

        var toUpdate = await InventoryItem.Upsert(new InventoryItem { Name = "Widget", Quantity = 5 });
        var toDelete = await InventoryItem.Upsert(new InventoryItem { Name = "Spare", Quantity = 2 });

        var batch = InventoryItem.Batch();
        batch.Add(new InventoryItem { Name = "New", Quantity = 7 });
        batch.Update(toUpdate.Id, item => item.Quantity = 11);
        batch.Delete(toDelete.Id);

        var result = await batch.Save();
        result.Added.Should().Be(1);
        result.Updated.Should().Be(1);
        result.Deleted.Should().Be(1);

        var remaining = await InventoryItem.All(partition);
        remaining.Should().HaveCount(2);
        remaining.Should().ContainSingle(item => item.Name == "Widget" && item.Quantity == 11);
        remaining.Should().ContainSingle(item => item.Name == "New" && item.Quantity == 7);
    }

    private sealed class InventoryItem : Entity<InventoryItem>
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
    }
}
