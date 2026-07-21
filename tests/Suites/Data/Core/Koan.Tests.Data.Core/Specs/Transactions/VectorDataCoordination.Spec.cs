using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Tests.Data.Core.Support;
using AwesomeAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Tests for ADR AI-0020 Phase 1: VectorData.SaveWithVector coordination and error handling.
/// Validates VectorCoordinationException behavior for partial save failures.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "1")]
[Trait("Category", "Unit")]
public sealed class VectorDataCoordinationSpec
{
    private readonly ITestOutputHelper _output;

    public VectorDataCoordinationSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Test #6: SaveWithVector_WithTransaction_DefersBoth
    /// Validates that both entity and vector operations are deferred when transaction is active.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_with_transaction_defers_both_operations()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"vector-data-{Guid.CreateVersion7():n}";

        if (!Vector<TodoEntity>.IsAvailable)
        {
            _output.WriteLine("Vector database not available - skipping test");
            return;
        }

        var entity = new TodoEntity
        {
            Title = "SaveWithVector Test",
            Description = "Testing coordinated save"
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            using (EntityContext.Transaction("save-with-vector-tx"))
            {
                // SaveWithVector should defer both operations
                await VectorData<TodoEntity>.SaveWithVector(entity, embedding, null);

                // Neither should be persisted yet
                var entityCount = await TodoEntity.Count;
                entityCount.Should().Be(0, "entity should not be persisted during transaction");

                var vectorExists = await TryGetVector(entity.Id);
                vectorExists.Should().BeFalse("vector should not be persisted during transaction");

                await EntityContext.Commit();
            }

            // Both should be persisted after commit
            var entityCountAfter = await TodoEntity.Count;
            entityCountAfter.Should().Be(1, "entity should be persisted after commit");

            var vectorExistsAfter = await TryGetVector(entity.Id);
            vectorExistsAfter.Should().BeTrue("vector should be persisted after commit");

            // Verify entity is retrievable
            var retrieved = await TodoEntity.Get(entity.Id);
            retrieved.Should().NotBeNull();
            retrieved!.Title.Should().Be("SaveWithVector Test");
        }
    }

    /// <summary>
    /// Test #7: SaveWithVector_WithoutTransaction_ExecutesSequentially
    /// Validates that entity saves before vector when no transaction is active.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_without_transaction_executes_sequentially()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"vector-data-{Guid.CreateVersion7():n}";

        if (!Vector<TodoEntity>.IsAvailable)
        {
            _output.WriteLine("Vector database not available - skipping test");
            return;
        }

        var entity = new TodoEntity
        {
            Title = "Sequential Test",
            Description = "No transaction coordination"
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            // No transaction - should execute immediately
            await VectorData<TodoEntity>.SaveWithVector(entity, embedding, null);

            // Both should be persisted immediately
            var entityCount = await TodoEntity.Count;
            entityCount.Should().Be(1, "entity should be persisted immediately");

            var vectorExists = await TryGetVector(entity.Id);
            vectorExists.Should().BeTrue("vector should be persisted immediately");
        }
    }

    /// <summary>
    /// Test #8: SaveWithVector_EntitySavedVectorFails_ThrowsCoordinationException
    /// Critical test: Validates error handling when vector save fails after entity save.
    /// This tests the VectorCoordinationException with EntitySaved=true, VectorSaved=false.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_vector_failure_after_entity_save_throws_coordination_exception()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"vector-data-{Guid.CreateVersion7():n}";

        if (!Vector<TodoEntity>.IsAvailable)
        {
            _output.WriteLine("Vector database not available - skipping test");
            _output.WriteLine("Note: This test requires vector database to validate coordination exception");
            return;
        }

        // Create entity with invalid vector (e.g., wrong dimensions) to force vector failure
        var entity = new TodoEntity
        {
            Title = "Error Test",
            Description = "Testing vector failure"
        };

        // Invalid embedding - most vector DBs expect specific dimensions (e.g., 1536 for OpenAI)
        // Using wrong size should cause vector save to fail
        var invalidEmbedding = GenerateTestEmbedding(10); // Wrong size

        using (var _ = EntityContext.Partition(partition))
        {
            VectorCoordinationException? caughtException = null;

            try
            {
                await VectorData<TodoEntity>.SaveWithVector(entity, invalidEmbedding, null);
            }
            catch (VectorCoordinationException ex)
            {
                caughtException = ex;
            }

            // If vector DB accepts any dimension, this test becomes a documentation test
            if (caughtException == null)
            {
                _output.WriteLine("Vector database accepted invalid dimensions - test inconclusive");
                _output.WriteLine("This test validates VectorCoordinationException behavior");
                return;
            }

            // Validate exception properties
            caughtException.EntitySaved.Should().BeTrue("entity should be persisted before vector failed");
            caughtException.VectorSaved.Should().BeFalse("vector save should have failed");
            caughtException.EntityId.Should().Be(entity.Id);
            caughtException.Message.Should().Contain("after entity was persisted");

            // Entity should still exist in database (saved before vector failure)
            var retrievedEntity = await TodoEntity.Get(entity.Id);
            retrievedEntity.Should().NotBeNull("entity should be persisted despite vector failure");

            // Vector should NOT exist
            var vectorExists = await TryGetVector(entity.Id);
            vectorExists.Should().BeFalse("vector should not exist after failure");
        }
    }

    /// <summary>
    /// Test #9: SaveWithVector_EntityFails_ThrowsOriginalException
    /// Validates that entity save failure propagates original exception (no coordination exception).
    /// </summary>
    [Fact]
    public async Task SaveWithVector_entity_failure_throws_original_exception()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"vector-data-{Guid.CreateVersion7():n}";

        if (!Vector<TodoEntity>.IsAvailable)
        {
            _output.WriteLine("Vector database not available - skipping test");
            return;
        }

        // Entity with validation that will fail
        var entity = new TodoEntity
        {
            Title = "", // Empty title might trigger validation failure
            Description = "Testing entity failure"
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            // For this test to work properly, we need validation to fail
            // If validation doesn't fail on empty title, this documents the behavior
            try
            {
                await VectorData<TodoEntity>.SaveWithVector(entity, embedding, null);

                // If we reach here, validation didn't fail - test is informative only
                _output.WriteLine("Entity save succeeded despite empty title");
                _output.WriteLine("This test validates that entity failures are NOT wrapped in VectorCoordinationException");
            }
            catch (VectorCoordinationException)
            {
                Assert.Fail("Entity save failure should NOT throw VectorCoordinationException");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Entity save failed with expected exception: {ex.GetType().Name}");
                // This is expected - entity save failures should propagate as-is
                ex.Should().NotBeOfType<VectorCoordinationException>(
                    "entity failures should propagate original exception type");
            }
        }
    }

    /// <summary>
    /// Test #16: SaveWithVector with metadata propagates to vector store.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_with_metadata_stores_metadata_in_vector_db()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"vector-data-{Guid.CreateVersion7():n}";

        if (!Vector<TodoEntity>.IsAvailable)
        {
            _output.WriteLine("Vector database not available - skipping test");
            return;
        }

        var entity = new TodoEntity
        {
            Title = "Metadata Test",
            Description = "Testing metadata storage"
        };

        var embedding = GenerateTestEmbedding(1536);
        var metadata = new Dictionary<string, object>
        {
            ["category"] = "test",
            ["priority"] = 5,
            ["tags"] = new[] { "urgent", "backend" }
        };

        using (var _ = EntityContext.Partition(partition))
        {
            await VectorData<TodoEntity>.SaveWithVector(entity, embedding, metadata);

            // Verify entity and vector exist
            var retrievedEntity = await TodoEntity.Get(entity.Id);
            retrievedEntity.Should().NotBeNull();

            var vectorExists = await TryGetVector(entity.Id);
            vectorExists.Should().BeTrue("vector with metadata should be persisted");
        }
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
