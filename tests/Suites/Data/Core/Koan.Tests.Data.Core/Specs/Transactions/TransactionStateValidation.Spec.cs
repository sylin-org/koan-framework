using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Transaction state validation tests for ADR AI-0020 Phase 1.
/// Validates EntityContext transaction lifecycle, state transitions, and operation queueing.
/// Addresses QA gap: "No transaction state validation tests"
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "1")]
[Trait("Category", "Unit")]
[Trait("Quality", "StateValidation")]
public sealed class TransactionStateValidationSpec
{
    private readonly ITestOutputHelper _output;

    public TransactionStateValidationSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"tx-state-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    /// <summary>
    /// Test: Operations executed immediately when no transaction is active.
    /// </summary>
    [Fact]
    public async Task Operations_execute_immediately_without_transaction()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Operations_execute_immediately_without_transaction))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Immediate Execution" };
                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    // No transaction - operations execute immediately
                    await entity.Save();

                    // Entity should be queryable immediately
                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "entity should be persisted immediately");

                    await Vector<TodoEntity>.Save(entity.Id, embedding);

                    // Vector should be persisted immediately
                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.ContainsVector(entity.Id).Should().BeTrue("vector should be persisted immediately");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Operations deferred during active transaction.
    /// </summary>
    [Fact]
    public async Task Operations_deferred_during_active_transaction()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Operations_deferred_during_active_transaction))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity1 = new TodoEntity { Title = "Deferred 1" };
                var entity2 = new TodoEntity { Title = "Deferred 2" };
                var embedding1 = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("defer-test"))
                    {
                        await entity1.Save();
                        await entity2.Save();
                        await Vector<TodoEntity>.Save(entity1.Id, embedding1);

                        // Nothing should be persisted yet
                        var count = await TodoEntity.Count;
                        count.Should().Be(0, "entities should not be persisted during transaction");

                        var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                        fakeRepo.VectorCount.Should().Be(0, "vectors should not be persisted during transaction");

                        await EntityContext.Commit();
                    }

                    // After commit - all operations should be persisted
                    var finalCount = await TodoEntity.Count;
                    finalCount.Should().Be(2, "both entities should be persisted after commit");

                    var fakeRepoAfterCommit = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepoAfterCommit.VectorCount.Should().Be(1, "vector should be persisted after commit");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Transaction isolation - operations in different partitions don't interfere.
    /// </summary>
    [Fact]
    public async Task Transactions_isolated_by_partition()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Transactions_isolated_by_partition))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition1 = $"partition1-{ctx.ExecutionId:n}";
                var partition2 = $"partition2-{ctx.ExecutionId:n}";
                ctx.SetItem("partition1", partition1);
                ctx.SetItem("partition2", partition2);
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition1 = ctx.GetRequiredItem<string>("partition1");
                var partition2 = ctx.GetRequiredItem<string>("partition2");

                var entity1 = new TodoEntity { Title = "Partition 1 Entity" };
                var entity2 = new TodoEntity { Title = "Partition 2 Entity" };

                // Transaction in partition1
                using (var _ = EntityContext.Partition(partition1))
                {
                    using (EntityContext.Transaction("tx1"))
                    {
                        await entity1.Save();

                        // Not committed yet
                        var count = await TodoEntity.Count;
                        count.Should().Be(0);

                        await EntityContext.Commit();
                    }

                    // Should be committed in partition1
                    var finalCount = await TodoEntity.Count;
                    finalCount.Should().Be(1);
                }

                // Separate transaction in partition2
                using (var _ = EntityContext.Partition(partition2))
                {
                    using (EntityContext.Transaction("tx2"))
                    {
                        await entity2.Save();
                        await EntityContext.Commit();
                    }

                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "partition2 should have 1 entity");
                }

                // Verify partition1 still has its entity
                using (var _ = EntityContext.Partition(partition1))
                {
                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "partition1 should still have 1 entity");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Commit without transaction does nothing (no-op).
    /// </summary>
    [Fact]
    public async Task Commit_without_transaction_is_noop()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Commit_without_transaction_is_noop))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                using (var _ = EntityContext.Partition(partition))
                {
                    // Commit without transaction should not throw
                    var act = async () => await EntityContext.Commit();
                    await act.Should().NotThrowAsync("commit without transaction should be no-op");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Rollback without transaction does nothing (no-op).
    /// </summary>
    [Fact]
    public async Task Rollback_without_transaction_is_noop()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Rollback_without_transaction_is_noop))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                using (var _ = EntityContext.Partition(partition))
                {
                    // Rollback without transaction should not throw
                    var act = async () => await EntityContext.Rollback();
                    await act.Should().NotThrowAsync("rollback without transaction should be no-op");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Double commit throws or is no-op (depending on implementation).
    /// </summary>
    [Fact]
    public async Task Double_commit_after_first_commit_is_noop()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Double_commit_after_first_commit_is_noop))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Double Commit Test" };

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("double-commit"))
                    {
                        await entity.Save();
                        await EntityContext.Commit();

                        // Second commit should be no-op (transaction already committed)
                        var act = async () => await EntityContext.Commit();
                        await act.Should().NotThrowAsync("double commit should be no-op");
                    }

                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "entity should be persisted once");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Rollback after commit is no-op.
    /// </summary>
    [Fact]
    public async Task Rollback_after_commit_is_noop()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Rollback_after_commit_is_noop))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Rollback After Commit" };

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("rollback-after-commit"))
                    {
                        await entity.Save();
                        await EntityContext.Commit();

                        // Rollback after commit should be no-op
                        var act = async () => await EntityContext.Rollback();
                        await act.Should().NotThrowAsync("rollback after commit should be no-op");
                    }

                    // Entity should still be persisted
                    var count = await TodoEntity.Count;
                    count.Should().Be(1, "entity should remain persisted");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Commit after rollback is no-op.
    /// </summary>
    [Fact]
    public async Task Commit_after_rollback_is_noop()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Commit_after_rollback_is_noop))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Commit After Rollback" };

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("commit-after-rollback"))
                    {
                        await entity.Save();
                        await EntityContext.Rollback();

                        // Commit after rollback should be no-op
                        var act = async () => await EntityContext.Commit();
                        await act.Should().NotThrowAsync("commit after rollback should be no-op");
                    }

                    // Entity should NOT be persisted
                    var count = await TodoEntity.Count;
                    count.Should().Be(0, "entity should not be persisted after rollback");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Transaction dispose without explicit commit/rollback defaults to rollback.
    /// </summary>
    [Fact]
    public async Task Transaction_dispose_without_commit_rolls_back()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Transaction_dispose_without_commit_rolls_back))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Implicit Rollback" };

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("implicit-rollback"))
                    {
                        await entity.Save();
                        // No commit or rollback - dispose should rollback
                    }

                    // Entity should NOT be persisted
                    var count = await TodoEntity.Count;
                    count.Should().Be(0, "entity should be rolled back on dispose");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Multiple operations queued and executed in order on commit.
    /// </summary>
    [Fact]
    public async Task Transaction_executes_operations_in_queue_order()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Transaction_executes_operations_in_queue_order))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity1 = new TodoEntity { Title = "First" };
                var entity2 = new TodoEntity { Title = "Second" };
                var entity3 = new TodoEntity { Title = "Third" };

                var embedding1 = GenerateTestEmbedding(1536);
                var embedding2 = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("queue-order"))
                    {
                        // Queue operations in specific order
                        await entity1.Save();
                        await Vector<TodoEntity>.Save(entity1.Id, embedding1);
                        await entity2.Save();
                        await entity3.Save();
                        await Vector<TodoEntity>.Save(entity2.Id, embedding2);

                        await EntityContext.Commit();
                    }

                    // Verify all operations committed
                    var entityCount = await TodoEntity.Count;
                    entityCount.Should().Be(3, "all entities should be persisted");

                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.VectorCount.Should().Be(2, "both vectors should be persisted");

                    // Verify operation tracking (order preserved)
                    var ops = fakeRepo.Operations;
                    ops.Should().HaveCount(2);
                    ops[0].Id.Should().Be(entity1.Id, "first vector operation should be entity1");
                    ops[1].Id.Should().Be(entity2.Id, "second vector operation should be entity2");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Empty transaction (no operations) commits successfully.
    /// </summary>
    [Fact]
    public async Task Empty_transaction_commits_successfully()
    {
        await TestPipeline.For<TransactionStateValidationSpec>(_output, nameof(Empty_transaction_commits_successfully))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("empty-tx"))
                    {
                        // No operations
                        var act = async () => await EntityContext.Commit();
                        await act.Should().NotThrowAsync("empty transaction should commit successfully");
                    }
                }
            })
            .Run();
    }

    #region Helper Methods

    private static float[] GenerateTestEmbedding(int dimensions)
    {
        var embedding = new float[dimensions];
        var random = new Random(42);

        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }

        return embedding;
    }

    #endregion
}
