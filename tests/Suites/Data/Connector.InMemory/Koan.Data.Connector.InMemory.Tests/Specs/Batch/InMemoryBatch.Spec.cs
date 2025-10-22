using System;
using Koan.Data.Core.Model;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Batch;

public sealed class InMemoryBatchSpec
{
    private readonly ITestOutputHelper _output;

    public InMemoryBatchSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Batch_operations_apply_atomically()
    {
        await TestPipeline.For<InMemoryBatchSpec>(_output, nameof(Batch_operations_apply_atomically))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<InventoryItem, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var toUpdate = await InventoryItem.UpsertAsync(new InventoryItem { Name = "Widget", Quantity = 5 });
                var toDelete = await InventoryItem.UpsertAsync(new InventoryItem { Name = "Spare", Quantity = 2 });

                var batch = InventoryItem.Batch();
                batch.Add(new InventoryItem { Name = "New", Quantity = 7 });
                batch.Update(toUpdate.Id, item => item.Quantity = 11);
                batch.Delete(toDelete.Id);

                var result = await batch.SaveAsync();
                result.Added.Should().Be(1);
                result.Updated.Should().Be(1);
                result.Deleted.Should().Be(1);

                var remaining = await InventoryItem.All(partition);
                remaining.Should().HaveCount(2);
                remaining.Should().ContainSingle(item => item.Name == "Widget" && item.Quantity == 11);
                remaining.Should().ContainSingle(item => item.Name == "New" && item.Quantity == 7);
            })
            .RunAsync();
    }

    private sealed class InventoryItem : Entity<InventoryItem>
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
