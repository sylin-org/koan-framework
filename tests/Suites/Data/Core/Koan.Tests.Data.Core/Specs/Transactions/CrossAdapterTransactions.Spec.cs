using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Cross-adapter transaction tests.
/// Tests coordinating operations across multiple adapters (e.g., SQLite + JSON).
/// </summary>
public sealed class CrossAdapterTransactionsSpec
{
    private readonly ITestOutputHelper _output;

    public CrossAdapterTransactionsSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"cross-adapter-tx-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    [Fact]
    public async Task Transaction_coordinates_saves_across_multiple_adapters()
    {
        await TestPipeline.For<CrossAdapterTransactionsSpec>(_output, nameof(Transaction_coordinates_saves_across_multiple_adapters))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entitySqlite = new TodoEntity
                {
                    Title = "SQLite Entity",
                    Description = "Saved to SQLite adapter"
                };

                var entityJson = new TodoEntity
                {
                    Title = "JSON Entity",
                    Description = "Saved to JSON adapter"
                };

                // Transaction coordinating across SQLite and JSON adapters
                using (EntityContext.Transaction("cross-adapter-test"))
                {
                    // Save to default adapter (SQLite)
                    using (EntityContext.Partition(partition))
                    {
                        await entitySqlite.Save();
                    }

                    // Save to JSON adapter
                    using (EntityContext.Adapter("json"))
                    using (EntityContext.Partition(partition))
                    {
                        await entityJson.Save();
                    }

                    // Commit both
                    await EntityContext.CommitAsync();
                }

                // Verify both were persisted
                using (EntityContext.Partition(partition))
                {
                    var retrievedSqlite = await TodoEntity.Get(entitySqlite.Id);
                    retrievedSqlite.Should().NotBeNull("SQLite entity should be persisted");
                }

                using (EntityContext.Adapter("json"))
                using (EntityContext.Partition(partition))
                {
                    var retrievedJson = await TodoEntity.Get(entityJson.Id);
                    retrievedJson.Should().NotBeNull("JSON entity should be persisted");
                }

                entitySqlite.Should().NotBeNull();
                entityJson.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_rollback_discards_changes_across_all_adapters()
    {
        await TestPipeline.For<CrossAdapterTransactionsSpec>(_output, nameof(Transaction_rollback_discards_changes_across_all_adapters))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity1 = new TodoEntity
                {
                    Title = "SQLite - Should Rollback",
                    Description = "This should not persist"
                };

                var entity2 = new TodoEntity
                {
                    Title = "JSON - Should Rollback",
                    Description = "This should also not persist"
                };

                // Transaction with rollback
                using (EntityContext.Transaction("cross-adapter-rollback"))
                {
                    // Track operations in SQLite
                    using (EntityContext.Partition(partition))
                    {
                        await entity1.Save();
                    }

                    // Track operations in JSON
                    using (EntityContext.Adapter("json"))
                    using (EntityContext.Partition(partition))
                    {
                        await entity2.Save();
                    }

                    // Rollback - discard all tracked operations
                    await EntityContext.RollbackAsync();
                }

                // Verify neither was persisted
                using (EntityContext.Partition(partition))
                {
                    var retrieved1 = await TodoEntity.Get(entity1.Id);
                    retrieved1.Should().BeNull("SQLite entity should not persist after rollback");
                }

                using (EntityContext.Adapter("json"))
                using (EntityContext.Partition(partition))
                {
                    var retrieved2 = await TodoEntity.Get(entity2.Id);
                    retrieved2.Should().BeNull("JSON entity should not persist after rollback");
                }

                entity1.Should().NotBeNull();
                entity2.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_groups_operations_by_adapter()
    {
        await TestPipeline.For<CrossAdapterTransactionsSpec>(_output, nameof(Transaction_groups_operations_by_adapter))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entities = Enumerable.Range(1, 10).Select(i => new TodoEntity
                {
                    Title = $"Entity {i}",
                    Description = $"Batch entity {i}"
                }).ToList();

                // Transaction with multiple operations on same adapter
                using (EntityContext.Transaction("grouped-operations"))
                {
                    using (EntityContext.Partition(partition))
                    {
                        // All these operations should be grouped together for the default adapter
                        foreach (var entity in entities)
                        {
                            await entity.Save();
                        }
                    }

                    await EntityContext.CommitAsync();
                }

                // Verify all were persisted
                using (EntityContext.Partition(partition))
                {
                    var count = await TodoEntity.Count;
                    count.Should().Be(10, "all entities should be persisted");

                    foreach (var entity in entities)
                    {
                        var retrieved = await TodoEntity.Get(entity.Id);
                        retrieved.Should().NotBeNull();
                    }
                }

                entities.Should().HaveCount(10);
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_capabilities_reflect_involved_adapters()
    {
        await TestPipeline.For<CrossAdapterTransactionsSpec>(_output, nameof(Transaction_capabilities_reflect_involved_adapters))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                TransactionCapabilities? capabilities = null;

                using (EntityContext.Transaction("capabilities-test"))
                {
                    // Track operations across adapters
                    using (EntityContext.Partition(partition))
                    {
                        await new TodoEntity { Title = "SQLite" }.Save();
                    }

                    using (EntityContext.Adapter("json"))
                    using (EntityContext.Partition(partition))
                    {
                        await new TodoEntity { Title = "JSON" }.Save();
                    }

                    // Get capabilities
                    capabilities = EntityContext.Capabilities;

                    await EntityContext.CommitAsync();
                }

                capabilities.Should().NotBeNull();
                capabilities!.Adapters.Should().Contain(a => a == "Default" || a == "json");
                capabilities.TrackedOperationCount.Should().Be(2);
                capabilities.SupportsLocalTransactions.Should().BeTrue();
                capabilities.SupportsDistributedTransactions.Should().BeFalse("best-effort atomicity only");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_with_mixed_saves_and_deletes()
    {
        await TestPipeline.For<CrossAdapterTransactionsSpec>(_output, nameof(Transaction_with_mixed_saves_and_deletes))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                // Create entities outside transaction
                var entityToUpdate = new TodoEntity { Title = "To Update", Description = "Original" };
                var entityToDelete = new TodoEntity { Title = "To Delete", Description = "Will be removed" };

                using (EntityContext.Partition(partition))
                {
                    await entityToUpdate.Save();
                    await entityToDelete.Save();
                }

                // Transaction with mixed operations
                var newEntity = new TodoEntity { Title = "New Entity", Description = "Created in transaction" };

                using (EntityContext.Transaction("mixed-operations"))
                {
                    using (EntityContext.Partition(partition))
                    {
                        // Update existing entity
                        entityToUpdate.Description = "Updated in transaction";
                        await entityToUpdate.Save();

                        // Delete entity
                        await entityToDelete.Remove();

                        // Create new entity
                        await newEntity.Save();
                    }

                    await EntityContext.CommitAsync();
                }

                // Verify operations
                using (EntityContext.Partition(partition))
                {
                    var updated = await TodoEntity.Get(entityToUpdate.Id);
                    updated.Should().NotBeNull();
                    updated!.Description.Should().Be("Updated in transaction");

                    var deleted = await TodoEntity.Get(entityToDelete.Id);
                    deleted.Should().BeNull("entity should be deleted");

                    var created = await TodoEntity.Get(newEntity.Id);
                    created.Should().NotBeNull();
                    created!.Title.Should().Be("New Entity");
                }

                entityToUpdate.Should().NotBeNull();
                entityToDelete.Should().NotBeNull();
                newEntity.Should().NotBeNull();
            })
            .RunAsync();
    }
}
