using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Tests for ADR AI-0020 Phase 1: Transaction Coordination for Vector Operations.
/// Validates that Vector&lt;T&gt;.Save/Delete participate in EntityContext transactions.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "1")]
[Trait("Category", "Unit")]
public sealed class VectorTransactionCoordinationSpec
{
    private readonly ITestOutputHelper _output;

    public VectorTransactionCoordinationSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"vector-tx-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    /// <summary>
    /// Test #1: TrackVectorSave_WithActiveTransaction_DefersExecution
    /// Validates that Vector.Save() defers execution when transaction is active.
    /// </summary>
    [Fact]
    public async Task Vector_save_within_transaction_defers_execution()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Vector_save_within_transaction_defers_execution))
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

                if (!Vector<TodoEntity>.IsAvailable)
                {
                    ctx.Diagnostics.Info("Vector database not available - skipping test");
                    return;
                }

                var entity = new TodoEntity
                {
                    Title = "Vector Transaction Test",
                    Description = "Testing deferred vector execution"
                };

                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    // Save entity first (outside transaction for setup)
                    await entity.Save();

                    // Inside transaction - vector save should be deferred
                    using (EntityContext.Transaction("test-vector-tx"))
                    {
                        await Vector<TodoEntity>.Save(entity.Id, embedding);

                        // Vector should NOT be queryable yet
                        var vectorExists = await TryGetVector(entity.Id);
                        vectorExists.Should().BeFalse("vector should not be persisted during transaction");

                        // Commit
                        await EntityContext.CommitAsync();
                    }

                    // After commit - vector should exist
                    var vectorExistsAfterCommit = await TryGetVector(entity.Id);
                    vectorExistsAfterCommit.Should().BeTrue("vector should be persisted after commit");
                }
            })
            .RunAsync();
    }

    /// <summary>
    /// Test #4: RollbackAsync_WithVectorOperations_DiscardsAll
    /// Validates that rollback discards pending vector operations.
    /// </summary>
    [Fact]
    public async Task Transaction_rollback_discards_vector_operations()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Transaction_rollback_discards_vector_operations))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                if (!Vector<TodoEntity>.IsAvailable)
                {
                    ctx.Diagnostics.Info("Vector database not available - skipping test");
                    return;
                }

                var entity = new TodoEntity
                {
                    Title = "Rollback Test",
                    Description = "Testing vector rollback"
                };

                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    await entity.Save(); // Save entity first

                    // Transaction with intentional rollback
                    using (EntityContext.Transaction("rollback-test"))
                    {
                        await Vector<TodoEntity>.Save(entity.Id, embedding);

                        // Rollback explicitly
                        await EntityContext.RollbackAsync();
                    }

                    // Vector should NOT exist after rollback
                    var vectorExists = await TryGetVector(entity.Id);
                    vectorExists.Should().BeFalse("vector should be discarded after rollback");
                }
            })
            .RunAsync();
    }

    /// <summary>
    /// Test #3: CommitAsync_WithVectorOperations_ExecutesAll
    /// Validates atomic commit of multiple entity + vector operations.
    /// </summary>
    [Fact]
    public async Task Transaction_commits_mixed_entity_and_vector_operations_atomically()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Transaction_commits_mixed_entity_and_vector_operations_atomically))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                if (!Vector<TodoEntity>.IsAvailable)
                {
                    ctx.Diagnostics.Info("Vector database not available - skipping test");
                    return;
                }

                var entity1 = new TodoEntity { Title = "Entity 1", Description = "First entity" };
                var entity2 = new TodoEntity { Title = "Entity 2", Description = "Second entity" };
                var entity3 = new TodoEntity { Title = "Entity 3", Description = "Third entity" };

                var embedding1 = GenerateTestEmbedding(1536);
                var embedding2 = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("mixed-ops-tx"))
                    {
                        // Mix of entity and vector operations
                        await entity1.Save();
                        await entity2.Save();
                        await Vector<TodoEntity>.Save(entity1.Id, embedding1);
                        await entity3.Save();
                        await Vector<TodoEntity>.Save(entity2.Id, embedding2);

                        // Nothing persisted yet
                        var entityCount = await TodoEntity.Count;
                        entityCount.Should().Be(0, "entities should not be persisted during transaction");

                        await EntityContext.CommitAsync();
                    }

                    // All operations should be committed
                    var finalEntityCount = await TodoEntity.Count;
                    finalEntityCount.Should().Be(3, "all entities should be persisted after commit");

                    var vector1Exists = await TryGetVector(entity1.Id);
                    var vector2Exists = await TryGetVector(entity2.Id);

                    vector1Exists.Should().BeTrue("vector1 should be persisted");
                    vector2Exists.Should().BeTrue("vector2 should be persisted");
                }
            })
            .RunAsync();
    }

    /// <summary>
    /// Test #13: Transaction_NestedTransactions_ThrowsNotSupported
    /// Validates that nested transactions are not allowed.
    /// </summary>
    [Fact]
    public async Task Nested_transactions_throw_not_supported_exception()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Nested_transactions_throw_not_supported_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
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
                    using (EntityContext.Transaction("outer-tx"))
                    {
                        // Attempt to start nested transaction
                        var act = () => EntityContext.Transaction("inner-tx");

                        act.Should().Throw<InvalidOperationException>()
                            .WithMessage("*nested transactions are not supported*");

                        await EntityContext.RollbackAsync();
                    }
                }
            })
            .RunAsync();
    }

    /// <summary>
    /// Test #14: Vector operations without transaction execute immediately.
    /// </summary>
    [Fact]
    public async Task Vector_save_without_transaction_executes_immediately()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Vector_save_without_transaction_executes_immediately))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                if (!Vector<TodoEntity>.IsAvailable)
                {
                    ctx.Diagnostics.Info("Vector database not available - skipping test");
                    return;
                }

                var entity = new TodoEntity { Title = "Immediate Test", Description = "No transaction" };
                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    await entity.Save();

                    // No transaction - should execute immediately
                    await Vector<TodoEntity>.Save(entity.Id, embedding);

                    // Vector should be queryable immediately
                    var vectorExists = await TryGetVector(entity.Id);
                    vectorExists.Should().BeTrue("vector should be persisted immediately without transaction");
                }
            })
            .RunAsync();
    }

    /// <summary>
    /// Test #15: Transaction with vector delete operations.
    /// </summary>
    [Fact]
    public async Task Transaction_defers_vector_delete_operations()
    {
        await TestPipeline.For<VectorTransactionCoordinationSpec>(_output, nameof(Transaction_defers_vector_delete_operations))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                if (!Vector<TodoEntity>.IsAvailable)
                {
                    ctx.Diagnostics.Info("Vector database not available - skipping test");
                    return;
                }

                var entity = new TodoEntity { Title = "Delete Test", Description = "Vector deletion" };
                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    // Setup: Create entity and vector outside transaction
                    await entity.Save();
                    await Vector<TodoEntity>.Save(entity.Id, embedding);

                    // Verify vector exists
                    var vectorExistsBefore = await TryGetVector(entity.Id);
                    vectorExistsBefore.Should().BeTrue("vector should exist before delete");

                    // Delete inside transaction - should be deferred
                    using (EntityContext.Transaction("delete-tx"))
                    {
                        await Vector<TodoEntity>.Delete(entity.Id);

                        // Vector should still exist during transaction
                        var vectorExistsDuring = await TryGetVector(entity.Id);
                        vectorExistsDuring.Should().BeTrue("vector should still exist during transaction");

                        await EntityContext.CommitAsync();
                    }

                    // Vector should be deleted after commit
                    var vectorExistsAfter = await TryGetVector(entity.Id);
                    vectorExistsAfter.Should().BeFalse("vector should be deleted after commit");
                }
            })
            .RunAsync();
    }

    #region Helper Methods

    private static float[] GenerateTestEmbedding(int dimensions)
    {
        var embedding = new float[dimensions];
        var random = new Random(42); // Fixed seed for deterministic tests

        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }

        return embedding;
    }

    private static async Task<bool> TryGetVector(string entityId)
    {
        try
        {
            // Try to query vector - if it throws or returns empty, vector doesn't exist
            var results = await Vector<TodoEntity>.Search(
                GenerateTestEmbedding(1536),
                topK: 10);

            return results.Matches.Any(r => r.Id == entityId);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
