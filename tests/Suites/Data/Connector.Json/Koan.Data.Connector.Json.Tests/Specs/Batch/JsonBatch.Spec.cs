using System;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json.Tests.Support;

namespace Koan.Data.Connector.Json.Tests.Specs.Batch;

public sealed class JsonBatchSpec
{
    private readonly ITestOutputHelper _output;

    public JsonBatchSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Batch_operations_apply_and_persist_changes()
    {
        await TestPipeline.For<JsonBatchSpec>(_output, nameof(Batch_operations_apply_and_persist_changes))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<InventoryItem, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var toUpdate = await InventoryItem.UpsertAsync(new InventoryItem { Name = "Widget", Quantity = 5 });
                var toDelete = await InventoryItem.UpsertAsync(new InventoryItem { Name = "Legacy", Quantity = 2 });

                var batch = InventoryItem.Batch();
                batch.Add(new InventoryItem { Name = "Fresh", Quantity = 7 });
                batch.Update(toUpdate.Id, item => item.Quantity = 11);
                batch.Delete(toDelete.Id);

                var result = await batch.SaveAsync();
                result.Added.Should().Be(1);
                result.Updated.Should().Be(1);
                result.Deleted.Should().Be(1);

                var remaining = await InventoryItem.All(partition);
                remaining.Should().HaveCount(2);
                remaining.Should().ContainSingle(item => item.Name == "Widget" && item.Quantity == 11);
                remaining.Should().ContainSingle(item => item.Name == "Fresh" && item.Quantity == 7);
            })
            .RunAsync();
    }

    [Fact]
    public async Task Batch_with_atomic_requirement_throws()
    {
        await TestPipeline.For<JsonBatchSpec>(_output, nameof(Batch_with_atomic_requirement_throws))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<InventoryItem, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var batch = InventoryItem.Batch();
                batch.Add(new InventoryItem { Name = "Atomic", Quantity = 1 });

                await FluentActions.Awaiting(() => batch.SaveAsync(new BatchOptions(RequireAtomic: true)))
                    .Should().ThrowAsync<NotSupportedException>();
            })
            .RunAsync();
    }

    private sealed class InventoryItem : Entity<InventoryItem>
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
