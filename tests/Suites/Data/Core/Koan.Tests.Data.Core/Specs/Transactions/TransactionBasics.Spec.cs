using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Basic transaction functionality tests.
/// Tests defer-and-commit, rollback, and auto-commit behaviors.
/// </summary>
public sealed class TransactionBasicsSpec
{
    private readonly ITestOutputHelper _output;

    public TransactionBasicsSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"data-core-transactions-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    [Fact]
    public async Task Transaction_defers_entity_saves_until_commit()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Transaction_defers_entity_saves_until_commit))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);

                return new { runtime, partition };
            })
            .Act(static async (ctx, input) =>
            {
                var (runtime, partition) = input;

                var entity1 = new TodoEntity
                {
                    Title = "Transaction Test 1",
                    Description = "Should be saved after commit"
                };

                var entity2 = new TodoEntity
                {
                    Title = "Transaction Test 2",
                    Description = "Should also be saved after commit"
                };

                using (var _ = EntityContext.Partition(partition))
                {
                    // Inside transaction - saves are deferred
                    using (EntityContext.Transaction("test-transaction"))
                    {
                        await entity1.Save();
                        await entity2.Save();

                        // Query immediately - entities should NOT be persisted yet
                        var countDuringTransaction = await TodoEntity.Count;
                        countDuringTransaction.Should().Be(0, "entities should not be persisted during transaction");

                        // Commit explicitly
                        await EntityContext.CommitAsync();
                    }

                    // After commit - entities should be persisted
                    var countAfterCommit = await TodoEntity.Count;
                    countAfterCommit.Should().Be(2, "entities should be persisted after commit");

                    var retrieved1 = await TodoEntity.Get(entity1.Id);
                    retrieved1.Should().NotBeNull();
                    retrieved1!.Title.Should().Be("Transaction Test 1");

                    var retrieved2 = await TodoEntity.Get(entity2.Id);
                    retrieved2.Should().NotBeNull();
                    retrieved2!.Title.Should().Be("Transaction Test 2");
                }

                return new { entity1, entity2 };
            })
            .Assert(static (ctx, input, output) =>
            {
                output.entity1.Should().NotBeNull();
                output.entity2.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_rollback_discards_pending_changes()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Transaction_rollback_discards_pending_changes))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);

                return new { runtime, partition };
            })
            .Act(static async (ctx, input) =>
            {
                var (runtime, partition) = input;

                var entity = new TodoEntity
                {
                    Title = "Should be rolled back",
                    Description = "This should never be persisted"
                };

                using (var _ = EntityContext.Partition(partition))
                {
                    // Transaction with rollback
                    using (EntityContext.Transaction("rollback-test"))
                    {
                        await entity.Save();

                        // Rollback explicitly
                        await EntityContext.RollbackAsync();
                    }

                    // After rollback - entity should NOT be persisted
                    var count = await TodoEntity.Count;
                    count.Should().Be(0, "entity should not be persisted after rollback");

                    var retrieved = await TodoEntity.Get(entity.Id);
                    retrieved.Should().BeNull("entity should not exist after rollback");
                }

                return entity;
            })
            .Assert(static (ctx, input, output) =>
            {
                output.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_auto_commits_on_dispose()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Transaction_auto_commits_on_dispose))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);

                return new { runtime, partition };
            })
            .Act(static async (ctx, input) =>
            {
                var (runtime, partition) = input;

                var entity = new TodoEntity
                {
                    Title = "Auto-commit test",
                    Description = "Should be auto-committed on dispose"
                };

                using (var _ = EntityContext.Partition(partition))
                {
                    // Transaction without explicit commit/rollback - should auto-commit
                    using (EntityContext.Transaction("auto-commit-test"))
                    {
                        await entity.Save();
                        // Dispose without commit/rollback - should auto-commit
                    }

                    // After dispose - entity should be persisted due to auto-commit
                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "entity should be auto-committed on dispose");

                    var retrieved = await TodoEntity.Get(entity.Id);
                    retrieved.Should().NotBeNull();
                    retrieved!.Title.Should().Be("Auto-commit test");
                }

                return entity;
            })
            .Assert(static (ctx, input, output) =>
            {
                output.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_tracks_delete_operations()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Transaction_tracks_delete_operations))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);

                return new { runtime, partition };
            })
            .Act(static async (ctx, input) =>
            {
                var (runtime, partition) = input;

                // First, create an entity outside transaction
                var entity = new TodoEntity
                {
                    Title = "To be deleted",
                    Description = "This will be deleted in transaction"
                };

                using (var _ = EntityContext.Partition(partition))
                {
                    await entity.Save();
                    var countBefore = await TodoEntity.Count;
                    countBefore.Should().Be(1);

                    // Delete in transaction
                    using (EntityContext.Transaction("delete-test"))
                    {
                        var deleted = await entity.Remove();
                        deleted.Should().BeTrue();

                        // Entity should still exist during transaction
                        var countDuringTransaction = await TodoEntity.Count;
                        countDuringTransaction.Should().Be(1, "entity should still exist during transaction");

                        await EntityContext.CommitAsync();
                    }

                    // After commit - entity should be deleted
                    var countAfter = await TodoEntity.Count;
                    countAfter.Should().Be(0, "entity should be deleted after commit");

                    var retrieved = await TodoEntity.Get(entity.Id);
                    retrieved.Should().BeNull();
                }

                return entity;
            })
            .Assert(static (ctx, input, output) =>
            {
                output.Should().NotBeNull();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Nested_transactions_throw_exception()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Nested_transactions_throw_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                return runtime;
            })
            .Act(static async (ctx, input) =>
            {
                var runtime = input;

                InvalidOperationException? exception = null;

                try
                {
                    using (EntityContext.Transaction("outer-transaction"))
                    {
                        // Attempt to nest transaction - should throw
                        using (EntityContext.Transaction("inner-transaction"))
                        {
                            // Should never reach here
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    exception = ex;
                }

                return exception;
            })
            .Assert(static (ctx, input, output) =>
            {
                output.Should().NotBeNull("nested transactions should throw InvalidOperationException");
                output!.Message.Should().Contain("nested", "error message should mention nested transactions");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Transaction_context_is_accessible()
    {
        await TestPipeline.For<TransactionBasicsSpec>(_output, nameof(Transaction_context_is_accessible))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                return runtime;
            })
            .Act(static async (ctx, input) =>
            {
                // Outside transaction
                EntityContext.InTransaction.Should().BeFalse();
                EntityContext.Current?.Transaction.Should().BeNull();

                bool inTransactionFlag = false;
                string? transactionName = null;

                // Inside transaction
                using (EntityContext.Transaction("context-test"))
                {
                    inTransactionFlag = EntityContext.InTransaction;
                    transactionName = EntityContext.Current?.Transaction;

                    await Task.CompletedTask;
                }

                // After transaction
                EntityContext.InTransaction.Should().BeFalse();

                return new { inTransactionFlag, transactionName };
            })
            .Assert(static (ctx, input, output) =>
            {
                output.inTransactionFlag.Should().BeTrue("should be in transaction inside using block");
                output.transactionName.Should().Be("context-test");
            })
            .RunAsync();
    }
}
