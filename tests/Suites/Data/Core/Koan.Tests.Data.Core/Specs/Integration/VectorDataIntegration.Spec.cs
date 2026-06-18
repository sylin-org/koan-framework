using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Tests.Data.Core.Support;
using AwesomeAssertions;

namespace Koan.Tests.Data.Core.Specs.Integration;

/// <summary>
/// End-to-end integration tests for ADR AI-0020.
/// Tests complete workflows: Entity save + Vector save + Embedding generation.
/// Addresses QA gap: "No integration tests"
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Category", "Integration")]
[Trait("Quality", "E2E")]
public sealed class VectorDataIntegrationSpec
{
    private readonly ITestOutputHelper _output;

    public VectorDataIntegrationSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Integration Test #1: Full workflow - Entity save + Vector save in transaction.
    /// </summary>
    [Fact]
    public async Task Complete_workflow_entity_and_vector_save_in_transaction()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article = new ArticleEntity
        {
            Title = "Vector Integration Test",
            Content = "This is a comprehensive test of the full vector workflow."
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            using (EntityContext.Transaction("e2e-test"))
            {
                // Save entity
                await article.Save();

                // Save vector
                await Vector<ArticleEntity>.Save(article.Id, embedding);

                // Verify nothing persisted yet
                var entityCount = await ArticleEntity.Count;
                entityCount.Should().Be(0, "entity should not be persisted during transaction");

                var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
                fakeRepo.VectorCount.Should().Be(0, "vector should not be persisted during transaction");

                // Commit
                await EntityContext.Commit();
            }

            // Verify both persisted
            var finalEntityCount = await ArticleEntity.Count;
            finalEntityCount.Should().Be(1, "entity should be persisted after commit");

            var savedArticle = await ArticleEntity.Get(article.Id);
            savedArticle.Should().NotBeNull();
            savedArticle!.Title.Should().Be("Vector Integration Test");

            var fakeRepoAfterCommit = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            fakeRepoAfterCommit.VectorCount.Should().Be(1, "vector should be persisted after commit");
            fakeRepoAfterCommit.ContainsVector(article.Id).Should().BeTrue();

            var retrievedVector = fakeRepoAfterCommit.GetVector(article.Id);
            retrievedVector.Should().NotBeNull();
            retrievedVector.Should().HaveCount(1536);
        }
    }

    /// <summary>
    /// Integration Test #2: SaveWithVector convenience method.
    /// </summary>
    [Fact]
    public async Task SaveWithVector_saves_entity_and_vector_atomically()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article = new ArticleEntity
        {
            Title = "SaveWithVector Test",
            Content = "Testing the convenience method"
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            // Single call saves both
            await VectorData<ArticleEntity>.SaveWithVector(article, embedding, null);

            // Verify both saved
            var savedArticle = await ArticleEntity.Get(article.Id);
            savedArticle.Should().NotBeNull();
            savedArticle!.Title.Should().Be("SaveWithVector Test");

            var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            fakeRepo.ContainsVector(article.Id).Should().BeTrue();
        }
    }

    /// <summary>
    /// Integration Test #3: Multiple entities with vectors in single transaction.
    /// </summary>
    [Fact]
    public async Task Transaction_saves_multiple_entities_with_vectors_atomically()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article1 = new ArticleEntity { Title = "Article 1", Content = "Content 1" };
        var article2 = new ArticleEntity { Title = "Article 2", Content = "Content 2" };
        var article3 = new ArticleEntity { Title = "Article 3", Content = "Content 3" };

        var embedding1 = GenerateTestEmbedding(1536);
        var embedding2 = GenerateTestEmbedding(1536);
        var embedding3 = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            using (EntityContext.Transaction("multi-entity-tx"))
            {
                await article1.Save();
                await Vector<ArticleEntity>.Save(article1.Id, embedding1);

                await article2.Save();
                await Vector<ArticleEntity>.Save(article2.Id, embedding2);

                await article3.Save();
                await Vector<ArticleEntity>.Save(article3.Id, embedding3);

                await EntityContext.Commit();
            }

            // Verify all entities saved
            var entityCount = await ArticleEntity.Count;
            entityCount.Should().Be(3, "all entities should be persisted");

            // Verify all vectors saved
            var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            fakeRepo.VectorCount.Should().Be(3, "all vectors should be persisted");

            fakeRepo.ContainsVector(article1.Id).Should().BeTrue();
            fakeRepo.ContainsVector(article2.Id).Should().BeTrue();
            fakeRepo.ContainsVector(article3.Id).Should().BeTrue();
        }
    }

    /// <summary>
    /// Integration Test #4: Rollback discards both entity and vector.
    /// </summary>
    [Fact]
    public async Task Transaction_rollback_discards_entity_and_vector()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article = new ArticleEntity
        {
            Title = "Rollback Test",
            Content = "This should be rolled back"
        };

        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            using (EntityContext.Transaction("rollback-tx"))
            {
                await article.Save();
                await Vector<ArticleEntity>.Save(article.Id, embedding);

                // Rollback
                await EntityContext.Rollback();
            }

            // Verify nothing persisted
            var entityCount = await ArticleEntity.Count;
            entityCount.Should().Be(0, "entity should be rolled back");

            var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            fakeRepo.VectorCount.Should().Be(0, "vector should be rolled back");
        }
    }

    /// <summary>
    /// Integration Test #5: Embedding generation from entity attributes.
    /// </summary>
    [Fact]
    public void EmbeddingMetadata_generates_text_from_entity_attributes()
    {
        var article = new ArticleEntity
        {
            Title = "AI-Powered Search",
            Content = "Vector databases enable semantic search capabilities."
        };

        var metadata = EmbeddingMetadata.Resolve<ArticleEntity>();
        var embeddingText = metadata.BuildEmbeddingText(article);

        embeddingText.Should().Contain("AI-Powered Search");
        embeddingText.Should().Contain("Vector databases enable semantic search capabilities.");
    }

    /// <summary>
    /// Integration Test #6: Vector search returns correct results.
    /// </summary>
    [Fact]
    public async Task Vector_search_returns_semantically_similar_results()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article1 = new ArticleEntity { Title = "Article 1" };
        var article2 = new ArticleEntity { Title = "Article 2" };
        var article3 = new ArticleEntity { Title = "Article 3" };

        // Use same embedding for article1 and article2 (should be similar)
        var sharedEmbedding = GenerateTestEmbedding(1536, seed: 42);
        var differentEmbedding = GenerateTestEmbedding(1536, seed: 999);

        using (var _ = EntityContext.Partition(partition))
        {
            await article1.Save();
            await Vector<ArticleEntity>.Save(article1.Id, sharedEmbedding);

            await article2.Save();
            await Vector<ArticleEntity>.Save(article2.Id, sharedEmbedding);

            await article3.Save();
            await Vector<ArticleEntity>.Save(article3.Id, differentEmbedding);

            // Search with shared embedding
            var results = await Vector<ArticleEntity>.Search(sharedEmbedding, topK: 3);

            results.Should().NotBeNull();
            results.Matches.Count.Should().BeGreaterThanOrEqualTo(2);

            // article1 and article2 should have higher similarity
            var topTwo = results.Matches.Take(2).ToList();
            topTwo.Should().Contain(m => m.Id == article1.Id);
            topTwo.Should().Contain(m => m.Id == article2.Id);
        }
    }

    /// <summary>
    /// Integration Test #7: Delete entity and vector together.
    /// </summary>
    [Fact]
    public async Task Delete_entity_and_vector_together()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article = new ArticleEntity { Title = "To Be Deleted" };
        var embedding = GenerateTestEmbedding(1536);

        using (var _ = EntityContext.Partition(partition))
        {
            // Create entity and vector
            await article.Save();
            await Vector<ArticleEntity>.Save(article.Id, embedding);

            // Verify created
            var savedArticle = await ArticleEntity.Get(article.Id);
            savedArticle.Should().NotBeNull();

            var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            fakeRepo.ContainsVector(article.Id).Should().BeTrue();

            // Delete both
            await article.Delete();
            await Vector<ArticleEntity>.Delete(article.Id);

            // Verify deleted
            var deletedArticle = await ArticleEntity.Get(article.Id);
            deletedArticle.Should().BeNull("entity should be deleted");

            fakeRepo.ContainsVector(article.Id).Should().BeFalse("vector should be deleted");
        }
    }

    /// <summary>
    /// Integration Test #8: Update entity and vector together.
    /// </summary>
    [Fact]
    public async Task Update_entity_and_vector_together()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        var partition = $"integration-{Guid.CreateVersion7():n}";

        var article = new ArticleEntity { Title = "Original Title", Content = "Original content" };
        var originalEmbedding = GenerateTestEmbedding(1536, seed: 1);

        using (var _ = EntityContext.Partition(partition))
        {
            // Create
            await article.Save();
            await Vector<ArticleEntity>.Save(article.Id, originalEmbedding);

            // Update
            article.Title = "Updated Title";
            article.Content = "Updated content";
            var updatedEmbedding = GenerateTestEmbedding(1536, seed: 2);

            await article.Save();
            await Vector<ArticleEntity>.Save(article.Id, updatedEmbedding);

            // Verify updates
            var savedArticle = await ArticleEntity.Get(article.Id);
            savedArticle.Should().NotBeNull();
            savedArticle!.Title.Should().Be("Updated Title");

            var fakeRepo = runtime.VectorService.GetFakeRepository<ArticleEntity, string>();
            var currentVector = fakeRepo.GetVector(article.Id);
            currentVector.Should().NotBeNull();

            // Vector should be updated (different from original)
            currentVector.Should().NotBeEquivalentTo(originalEmbedding, "vector should be updated");
        }
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

    #region Test Entities

    [Embedding(Template = "{Title}\n\n{Content}")]
    public class ArticleEntity : Entity<ArticleEntity>
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
