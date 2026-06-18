using System;

namespace Koan.Data.Connector.Json.Tests.Specs.Batch;

public sealed class JsonBatchSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Batch_operations_apply_and_persist_changes()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("batch");
        using var lease = Lease(partition);

        var toUpdate = await InventoryItem.Upsert(new InventoryItem { Name = "Widget", Quantity = 5 });
        var toDelete = await InventoryItem.Upsert(new InventoryItem { Name = "Legacy", Quantity = 2 });

        var batch = InventoryItem.Batch();
        batch.Add(new InventoryItem { Name = "Fresh", Quantity = 7 });
        batch.Update(toUpdate.Id, item => item.Quantity = 11);
        batch.Delete(toDelete.Id);

        var result = await batch.Save();
        result.Added.Should().Be(1);
        result.Updated.Should().Be(1);
        result.Deleted.Should().Be(1);

        var remaining = await InventoryItem.All(partition);
        remaining.Should().HaveCount(2);
        remaining.Should().ContainSingle(item => item.Name == "Widget" && item.Quantity == 11);
        remaining.Should().ContainSingle(item => item.Name == "Fresh" && item.Quantity == 7);
    }

    [Fact]
    public async Task Batch_with_atomic_requirement_throws()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("batch");
        using var lease = Lease(partition);

        var batch = InventoryItem.Batch();
        batch.Add(new InventoryItem { Name = "Atomic", Quantity = 1 });

        await FluentActions.Awaiting(() => batch.Save(new BatchOptions(RequireAtomic: true)))
            .Should().ThrowAsync<NotSupportedException>();
    }

    private sealed class InventoryItem : Entity<InventoryItem>
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
    }
}
