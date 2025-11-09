using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Transaction error handling and edge case tests.
/// Tests exception handling, partial failures, and edge cases.
/// </summary>
public sealed class TransactionErrorHandlingSpec
{
    private readonly ITestOutputHelper _output;

    public TransactionErrorHandlingSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"tx-error-handling-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    [Fact]
    public async Task Commit_without_transaction_throws_exception()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Commit_without_transaction_throws_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Assert(static async _ =>
            {
                InvalidOperationException? exception = null;

                try
                {
                    // Attempt to commit without transaction
                    await EntityContext.CommitAsync();
                }
                catch (InvalidOperationException ex)
                {
                    exception = ex;
                }

                exception.Should().NotBeNull("commit without transaction should throw");
                exception!.Message.Should().Contain("No active transaction");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Rollback_without_transaction_throws_exception()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Rollback_without_transaction_throws_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Assert(static async _ =>
            {
                InvalidOperationException? exception = null;

                try
                {
                    // Attempt to rollback without transaction
                    await EntityContext.RollbackAsync();
                }
                catch (InvalidOperationException ex)
                {
                    exception = ex;
                }

                exception.Should().NotBeNull("rollback without transaction should throw");
                exception!.Message.Should().Contain("No active transaction");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Double_commit_throws_exception()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Double_commit_throws_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Assert(static async _ =>
            {
                InvalidOperationException? exception = null;

                try
                {
                    using (EntityContext.Transaction("double-commit-test"))
                    {
                        await EntityContext.CommitAsync();

                        // Attempt to commit again
                        await EntityContext.CommitAsync();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    exception = ex;
                }

                exception.Should().NotBeNull("double commit should throw");
                exception!.Message.Should().Contain("already been completed");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Empty_transaction_commits_successfully()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Empty_transaction_commits_successfully))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Assert(static async _ =>
            {
                var success = false;

                // Transaction with no operations
                using (EntityContext.Transaction("empty-transaction"))
                {
                    await EntityContext.CommitAsync();
                    success = true;
                }

                success.Should().BeTrue("empty transaction should commit successfully");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_with_same_entity_saved_multiple_times()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Transaction_with_same_entity_saved_multiple_times))
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

                var entity = new TodoEntity { Title = "Original", Description = "Version 1" };

                using (EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("multiple-saves"))
                    {
                        await entity.Save();

                        entity.Description = "Version 2";
                        await entity.Save();

                        entity.Description = "Version 3";
                        await entity.Save();

                        await EntityContext.CommitAsync();
                    }

                    // Verify final version was persisted
                    var retrieved = await TodoEntity.Get(entity.Id);
                    retrieved.Should().NotBeNull();
                    retrieved!.Description.Should().Be("Version 3", "last save should win");
                }

                entity.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_context_restored_after_exception()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Transaction_context_restored_after_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Assert(static async _ =>
            {
                var beforeTransaction = EntityContext.InTransaction;

                try
                {
                    using (EntityContext.Transaction("exception-test"))
                    {
                        // Throw exception inside transaction
                        throw new InvalidOperationException("Simulated error");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Expected
                }

                var afterTransaction = EntityContext.InTransaction;

                beforeTransaction.Should().BeFalse();
                afterTransaction.Should().BeFalse("context should be restored after exception");

                await Task.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_with_partition_routing()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Transaction_with_partition_routing))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition1 = $"partition-1-{ctx.ExecutionId:n}";
                var partition2 = $"partition-2-{ctx.ExecutionId:n}";
                ctx.SetItem("partition1", partition1);
                ctx.SetItem("partition2", partition2);
            })
            .Assert(static async ctx =>
            {
                var partition1 = ctx.GetRequiredItem<string>("partition1");
                var partition2 = ctx.GetRequiredItem<string>("partition2");

                var entity1 = new TodoEntity { Title = "Partition 1", Description = "In partition 1" };
                var entity2 = new TodoEntity { Title = "Partition 2", Description = "In partition 2" };

                // Transaction across multiple partitions
                using (EntityContext.Transaction("multi-partition"))
                {
                    using (EntityContext.Partition(partition1))
                    {
                        await entity1.Save();
                    }

                    using (EntityContext.Partition(partition2))
                    {
                        await entity2.Save();
                    }

                    await EntityContext.CommitAsync();
                }

                // Verify entities in respective partitions
                using (EntityContext.Partition(partition1))
                {
                    var retrieved1 = await TodoEntity.Get(entity1.Id);
                    retrieved1.Should().NotBeNull("entity should be in partition 1");

                    var count1 = await TodoEntity.Count;
                    count1.Should().Be(1);
                }

                using (EntityContext.Partition(partition2))
                {
                    var retrieved2 = await TodoEntity.Get(entity2.Id);
                    retrieved2.Should().NotBeNull("entity should be in partition 2");

                    var count2 = await TodoEntity.Count;
                    count2.Should().Be(1);
                }

                entity1.Should().NotBeNull();
                entity2.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_with_adapter_and_partition_routing()
    {
        await TestPipeline.For<TransactionErrorHandlingSpec>(_output, nameof(Transaction_with_adapter_and_partition_routing))
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

                var entity1 = new TodoEntity { Title = "SQLite + Partition", Description = "Default adapter with partition" };
                var entity2 = new TodoEntity { Title = "JSON + Partition", Description = "JSON adapter with partition" };

                // Transaction with adapter AND partition routing
                using (EntityContext.Transaction("adapter-partition"))
                {
                    // Default adapter with partition
                    using (EntityContext.Partition(partition))
                    {
                        await entity1.Save();
                    }

                    // JSON adapter with partition
                    using (EntityContext.Adapter("json"))
                    using (EntityContext.Partition(partition))
                    {
                        await entity2.Save();
                    }

                    await EntityContext.CommitAsync();
                }

                // Verify entities
                using (EntityContext.Partition(partition))
                {
                    var retrieved1 = await TodoEntity.Get(entity1.Id);
                    retrieved1.Should().NotBeNull();
                }

                using (EntityContext.Adapter("json"))
                using (EntityContext.Partition(partition))
                {
                    var retrieved2 = await TodoEntity.Get(entity2.Id);
                    retrieved2.Should().NotBeNull();
                }

                entity1.Should().NotBeNull();
                entity2.Should().NotBeNull();
            })
            .RunAsync();
    }
}
