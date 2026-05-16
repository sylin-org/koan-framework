using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Tests.Data.Core.Support;
using FluentAssertions;
using static Koan.Tests.Data.Core.Specs.Concurrency.VectorConcurrencySpec;

namespace Koan.Tests.Data.Core.Specs.Concurrency;

/// <summary>
/// Concurrency smoke tests for ADR AI-0020.
/// Validates basic thread safety of vector operations and transactions.
/// Addresses QA gap: "No concurrency tests"
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Category", "Integration")]
[Trait("Quality", "Concurrency")]
public sealed class VectorConcurrencySpec
{
    private readonly ITestOutputHelper _output;

    public VectorConcurrencySpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"concurrency-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    /// <summary>
    /// Concurrency Test #1: Concurrent entity saves without transactions.
    /// </summary>
    [Fact]
    public async Task Concurrent_entity_saves_without_transactions()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_entity_saves_without_transactions))
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
                    // Create 10 entities concurrently
                    var tasks = Enumerable.Range(0, 10)
                        .Select(i => Task.Run(async () =>
                        {
                            var entity = new TodoEntity { Title = $"Concurrent Entity {i}" };
                            await entity.Save();
                            return entity.Id;
                        }))
                        .ToList();

                    var entityIds = await Task.WhenAll(tasks);

                    // Verify all entities saved
                    var count = await TodoEntity.Count;
                    count.Should().Be(10, "all entities should be saved concurrently");

                    // Verify all IDs are unique
                    entityIds.Should().OnlyHaveUniqueItems("all entity IDs should be unique");
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #2: Concurrent vector saves without transactions.
    /// </summary>
    [Fact]
    public async Task Concurrent_vector_saves_without_transactions()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_vector_saves_without_transactions))
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

                using (var _ = EntityContext.Partition(partition))
                {
                    // Create entities first
                    var entities = Enumerable.Range(0, 10)
                        .Select(i => new TodoEntity { Title = $"Entity {i}" })
                        .ToList();

                    foreach (var entity in entities)
                    {
                        await entity.Save();
                    }

                    // Save vectors concurrently
                    var tasks = entities.Select(entity => Task.Run(async () =>
                    {
                        var embedding = GenerateTestEmbedding(1536, seed: entity.Id.GetHashCode());
                        await Vector<TodoEntity>.Save(entity.Id, embedding);
                    }))
                    .ToList();

                    await Task.WhenAll(tasks);

                    // Verify all vectors saved
                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.VectorCount.Should().Be(10, "all vectors should be saved concurrently");
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #3: Concurrent transactions in different partitions.
    /// </summary>
    [Fact]
    public async Task Concurrent_transactions_in_different_partitions()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_transactions_in_different_partitions))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                // Don't set a single partition - each task will use its own
            })
            .Assert(static async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");

                // Run 5 transactions in parallel, each in a different partition
                var tasks = Enumerable.Range(0, 5)
                    .Select(i => Task.Run(async () =>
                    {
                        var partition = $"partition-{i}-{Guid.NewGuid():n}";
                        using (var _ = EntityContext.Partition(partition))
                        {
                            using (EntityContext.Transaction($"tx-{i}"))
                            {
                                var entity = new TodoEntity { Title = $"TX Entity {i}" };
                                var embedding = GenerateTestEmbedding(1536, seed: i);

                                await entity.Save();
                                await Vector<TodoEntity>.Save(entity.Id, embedding);

                                await EntityContext.Commit();

                                return (Partition: partition, EntityId: entity.Id);
                            }
                        }
                    }))
                    .ToList();

                var results = await Task.WhenAll(tasks);

                // Verify all transactions committed successfully
                results.Should().HaveCount(5);

                // Verify each partition has 1 entity
                foreach (var (partition, entityId) in results)
                {
                    using (var _ = EntityContext.Partition(partition))
                    {
                        var count = await TodoEntity.Count;
                        count.Should().Be(1, $"partition {partition} should have 1 entity");

                        var savedEntity = await TodoEntity.Get(entityId);
                        savedEntity.Should().NotBeNull();
                    }
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #4: Concurrent reads and writes (no transaction).
    /// </summary>
    [Fact]
    public async Task Concurrent_reads_and_writes_without_transaction()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_reads_and_writes_without_transaction))
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
                    // Create 10 entities first
                    var entities = Enumerable.Range(0, 10)
                        .Select(i => new TodoEntity { Title = $"Entity {i}" })
                        .ToList();

                    foreach (var entity in entities)
                    {
                        await entity.Save();
                    }

                    // Concurrent reads and writes
                    var writeTasks = entities.Take(5).Select(entity => Task.Run(async () =>
                    {
                        entity.Title = $"Updated {entity.Title}";
                        await entity.Save();
                    }));

                    var readTasks = entities.Skip(5).Select(entity => Task.Run(async () =>
                    {
                        var loaded = await TodoEntity.Get(entity.Id);
                        return loaded;
                    }));

                    var allTasks = writeTasks.Concat(readTasks).ToList();
                    await Task.WhenAll(allTasks);

                    // Verify writes succeeded
                    foreach (var entity in entities.Take(5))
                    {
                        var loaded = await TodoEntity.Get(entity.Id);
                        loaded.Should().NotBeNull();
                        loaded!.Title.Should().StartWith("Updated");
                    }
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #5: Concurrent SaveWithVector calls.
    /// </summary>
    [Fact]
    public async Task Concurrent_save_with_vector_calls()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_save_with_vector_calls))
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

                using (var _ = EntityContext.Partition(partition))
                {
                    // Create 10 entities with vectors concurrently
                    var tasks = Enumerable.Range(0, 10)
                        .Select(i => Task.Run(async () =>
                        {
                            var entity = new TodoEntity { Title = $"Concurrent SaveWithVector {i}" };
                            var embedding = GenerateTestEmbedding(1536, seed: i);

                            await VectorData<TodoEntity>.SaveWithVector(entity, embedding, null);

                            return entity.Id;
                        }))
                        .ToList();

                    var entityIds = await Task.WhenAll(tasks);

                    // Verify all entities saved
                    var entityCount = await TodoEntity.Count;
                    entityCount.Should().Be(10, "all entities should be saved concurrently");

                    // Verify all vectors saved
                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.VectorCount.Should().Be(10, "all vectors should be saved concurrently");

                    // Verify all IDs unique
                    entityIds.Should().OnlyHaveUniqueItems();
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #6: Stress test - many concurrent operations.
    /// </summary>
    [Fact]
    public async Task Stress_test_many_concurrent_operations()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Stress_test_many_concurrent_operations))
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

                using (var _ = EntityContext.Partition(partition))
                {
                    // 100 concurrent operations
                    var tasks = Enumerable.Range(0, 100)
                        .Select(i => Task.Run(async () =>
                        {
                            var entity = new TodoEntity { Title = $"Stress Test {i}" };
                            var embedding = GenerateTestEmbedding(1536, seed: i);

                            await entity.Save();
                            await Vector<TodoEntity>.Save(entity.Id, embedding);

                            return entity.Id;
                        }))
                        .ToList();

                    var entityIds = await Task.WhenAll(tasks);

                    // Verify all operations completed
                    var entityCount = await TodoEntity.Count;
                    entityCount.Should().Be(100, "all entities should be saved");

                    var fakeRepo = runtime.VectorService.GetFakeRepository<TodoEntity, string>();
                    fakeRepo.VectorCount.Should().Be(100, "all vectors should be saved");

                    entityIds.Should().OnlyHaveUniqueItems("all IDs should be unique");
                }
            })
            .Run();
    }

    /// <summary>
    /// Concurrency Test #7: Concurrent vector searches (read-only).
    /// </summary>
    [Fact]
    public async Task Concurrent_vector_searches()
    {
        await TestPipeline.For<VectorConcurrencySpec>(_output, nameof(Concurrent_vector_searches))
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
                    // Create 10 entities with vectors
                    var entities = Enumerable.Range(0, 10)
                        .Select(i => new TodoEntity { Title = $"Search Entity {i}" })
                        .ToList();

                    foreach (var entity in entities)
                    {
                        await entity.Save();
                        var embedding = GenerateTestEmbedding(1536, seed: entity.Id.GetHashCode());
                        await Vector<TodoEntity>.Save(entity.Id, embedding);
                    }

                    // Perform 20 concurrent searches
                    var searchTasks = Enumerable.Range(0, 20)
                        .Select(i => Task.Run(async () =>
                        {
                            var queryVector = GenerateTestEmbedding(1536, seed: i);
                            var results = await Vector<TodoEntity>.Search(queryVector, topK: 5);
                            return results.Matches.Count;
                        }))
                        .ToList();

                    var resultCounts = await Task.WhenAll(searchTasks);

                    // Verify all searches completed successfully
                    resultCounts.Should().AllSatisfy(count =>
                        count.Should().BeGreaterThan(0, "search should return results"));
                }
            })
            .Run();
    }

    #region Helper Methods

    private static float[] GenerateTestEmbedding(int dimensions, int seed = 42)
    {
        var embedding = new float[dimensions];
        var random = new Random(seed);

        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }

        return embedding;
    }

    #endregion
}
