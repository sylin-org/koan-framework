using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Deterministic error injection tests for ADR AI-0020 Phase 1.
/// Uses FakeVectorRepository for reliable failure scenario testing.
/// Addresses critical issue: VectorCoordinationException tests were "inconclusive" with real vector DBs.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "1")]
[Trait("Category", "Unit")]
[Trait("Quality", "ErrorInjection")]
public sealed class VectorErrorInjectionSpec
{
    private readonly ITestOutputHelper _output;

    public VectorErrorInjectionSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"vector-error-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    /// <summary>
    /// CRITICAL TEST: VectorCoordinationException with deterministic error injection.
    /// Previously "inconclusive" because real vector DBs might accept invalid dimensions.
    /// Now uses FakeVectorRepository with RequiredEmbeddingDimension validation.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_vector_upsert_failure_throws_coordination_exception_with_entity_saved()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(SaveWithVector_vector_upsert_failure_throws_coordination_exception_with_entity_saved))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                // Configure fake repository to reject invalid dimensions (DETERMINISTIC)
                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.RequiredEmbeddingDimension = 1536;
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity
                {
                    Title = "VectorCoordinationException Test",
                    Description = "Deterministic failure scenario"
                };

                // Invalid embedding dimensions (expected: 1536, provided: 384)
                var invalidEmbedding = GenerateTestEmbedding(384);

                VectorCoordinationException? caughtException = null;

                using (var _ = EntityContext.Partition(partition))
                {
                    try
                    {
                        await VectorData<TodoEntity>.SaveWithVector(entity, invalidEmbedding, null);
                    }
                    catch (VectorCoordinationException ex)
                    {
                        caughtException = ex;
                    }
                }

                // Assertions (NO MORE "inconclusive" escapes!)
                caughtException.Should().NotBeNull("vector upsert should fail with invalid dimensions");
                caughtException!.EntitySaved.Should().BeTrue("entity save should succeed before vector save");
                caughtException.VectorSaved.Should().BeFalse("vector save should fail");
                caughtException.InnerException.Should().NotBeNull("should contain original ArgumentException");
                caughtException.InnerException.Should().BeOfType<ArgumentException>("dimension validation throws ArgumentException");

                // Verify entity was actually saved (orphaned entity scenario)
                using (var _ = EntityContext.Partition(partition))
                {
                    var savedEntity = await TodoEntity.Get(entity.Id);
                    savedEntity.Should().NotBeNull("entity should be persisted even though vector failed");
                    savedEntity!.Title.Should().Be("VectorCoordinationException Test");
                }

                // Verify vector was NOT saved
                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.ContainsVector(entity.Id).Should().BeFalse("vector should not be persisted");
            })
            .Run();
    }

    /// <summary>
    /// Test: Custom exception propagation through VectorCoordinationException.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_custom_vector_exception_wrapped_in_coordination_exception()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(SaveWithVector_custom_vector_exception_wrapped_in_coordination_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                // Configure fake repository to throw custom exception
                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.ThrowOnUpsert = true;
                fakeRepo.CustomException = new InvalidOperationException("Vector database quota exceeded");
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Custom Exception Test" };
                var embedding = GenerateTestEmbedding(1536);

                VectorCoordinationException? caughtException = null;

                using (var _ = EntityContext.Partition(partition))
                {
                    try
                    {
                        await VectorData<TodoEntity>.SaveWithVector(entity, embedding, null);
                    }
                    catch (VectorCoordinationException ex)
                    {
                        caughtException = ex;
                    }
                }

                caughtException.Should().NotBeNull();
                caughtException!.InnerException.Should().BeOfType<InvalidOperationException>();
                caughtException.InnerException!.Message.Should().Contain("quota exceeded");
            })
            .Run();
    }

    /// <summary>
    /// Test: Vector.Save throws immediately (not wrapped) when called outside SaveWithVector.
    /// </summary>
    [Fact]
    public async Task Vector_save_failure_outside_coordination_throws_unwrapped_exception()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(Vector_save_failure_outside_coordination_throws_unwrapped_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.RequiredEmbeddingDimension = 1536;
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Direct Vector.Save Test" };
                var invalidEmbedding = GenerateTestEmbedding(128);

                using (var _ = EntityContext.Partition(partition))
                {
                    await entity.Save(); // Save entity separately

                    // Direct Vector.Save should throw ArgumentException (NOT VectorCoordinationException)
                    var act = async () => await Vector<TodoEntity>.Save(entity.Id, invalidEmbedding);

                    await act.Should().ThrowAsync<ArgumentException>()
                        .WithMessage("*Invalid embedding dimension*");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Vector delete failure scenarios.
    /// </summary>
    [Fact]
    public async Task Vector_delete_failure_propagates_exception()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(Vector_delete_failure_propagates_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                // Configure repository to fail on delete
                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.ThrowOnDelete = true;
                fakeRepo.CustomException = new InvalidOperationException("Vector delete operation not permitted");
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                using (var _ = EntityContext.Partition(partition))
                {
                    var act = async () => await Vector<TodoEntity>.Delete("test-id-123");

                    await act.Should().ThrowAsync<InvalidOperationException>()
                        .WithMessage("*not permitted*");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Transaction rollback after vector save error discards both operations.
    /// </summary>
    [Fact]
    public async Task Transaction_rollback_after_vector_error_discards_entity_and_vector()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(Transaction_rollback_after_vector_error_discards_entity_and_vector))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.RequiredEmbeddingDimension = 1536;
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity = new TodoEntity { Title = "Rollback Test" };
                var invalidEmbedding = GenerateTestEmbedding(64); // Wrong dimension

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("rollback-on-error"))
                    {
                        await entity.Save();

                        try
                        {
                            await Vector<TodoEntity>.Save(entity.Id, invalidEmbedding);
                        }
                        catch (ArgumentException)
                        {
                            // Expected error - rollback transaction
                            await EntityContext.Rollback();
                        }
                    }

                    // Both entity and vector should be discarded
                    var savedEntity = await TodoEntity.Get(entity.Id);
                    savedEntity.Should().BeNull("entity should be rolled back");

                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.ContainsVector(entity.Id).Should().BeFalse("vector should be rolled back");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Vector search failure propagates correctly.
    /// </summary>
    [Fact]
    public async Task Vector_search_failure_throws_configured_exception()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(Vector_search_failure_throws_configured_exception))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.ThrowOnSearch = true;
                fakeRepo.CustomException = new TimeoutException("Vector search timeout");
            })
            .Assert(static async ctx =>
            {
                var partition = ctx.GetRequiredItem<string>("partition");

                using (var _ = EntityContext.Partition(partition))
                {
                    var queryVector = GenerateTestEmbedding(1536);

                    var act = async () => await Vector<TodoEntity>.Search(queryVector, topK: 10);

                    await act.Should().ThrowAsync<TimeoutException>()
                        .WithMessage("*timeout*");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Multiple vector operations in transaction with one failure rolls back all.
    /// </summary>
    [Fact]
    public async Task Transaction_with_mixed_operations_one_vector_failure_rolls_back_all()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(Transaction_with_mixed_operations_one_vector_failure_rolls_back_all))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = EnsurePartition(ctx);
                ctx.SetItem("partition", partition);

                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.RequiredEmbeddingDimension = 1536;
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partition = ctx.GetRequiredItem<string>("partition");

                var entity1 = new TodoEntity { Title = "Entity 1" };
                var entity2 = new TodoEntity { Title = "Entity 2" };
                var entity3 = new TodoEntity { Title = "Entity 3" };

                var validEmbedding1 = GenerateTestEmbedding(1536);
                var invalidEmbedding = GenerateTestEmbedding(256); // Wrong dimension
                var validEmbedding2 = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    using (EntityContext.Transaction("multi-op-tx"))
                    {
                        await entity1.Save();
                        await Vector<TodoEntity>.Save(entity1.Id, validEmbedding1);

                        await entity2.Save();

                        try
                        {
                            // This should fail
                            await Vector<TodoEntity>.Save(entity2.Id, invalidEmbedding);
                        }
                        catch (ArgumentException)
                        {
                            // Expected - rollback
                            await EntityContext.Rollback();
                        }

                        // entity3 operations never execute due to rollback
                    }

                    // Verify ALL operations were rolled back
                    var count = await TodoEntity.Count;
                    count.Should().Be(0, "all entity saves should be rolled back");

                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.VectorCount.Should().Be(0, "all vector saves should be rolled back");
                }
            })
            .Run();
    }

    /// <summary>
    /// Test: Verify FakeVectorRepository operations tracking.
    /// </summary>
    [Fact]
    public async Task FakeVectorRepository_tracks_all_operations_for_inspection()
    {
        await TestPipeline.For<VectorErrorInjectionSpec>(_output, nameof(FakeVectorRepository_tracks_all_operations_for_inspection))
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

                var entity = new TodoEntity { Title = "Operations Tracking Test" };
                var embedding = GenerateTestEmbedding(1536);

                using (var _ = EntityContext.Partition(partition))
                {
                    await entity.Save();
                    await Vector<TodoEntity>.Save(entity.Id, embedding);
                }

                // Inspect operations log
                var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                fakeRepo.Operations.Should().HaveCount(1, "one upsert operation should be tracked");

                var op = fakeRepo.Operations[0];
                op.Id.Should().Be(entity.Id);
                op.Embedding.Should().HaveCount(1536);
            })
            .Run();
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

    #endregion
}
