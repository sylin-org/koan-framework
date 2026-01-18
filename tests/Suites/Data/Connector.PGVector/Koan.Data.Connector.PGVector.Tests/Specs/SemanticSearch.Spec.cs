using FluentAssertions;
using Xunit;
using Koan.Data.Connector.PGVector.Tests.Support;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// Integration tests for semantic search scenarios.
/// Tests real-world use cases: document similarity, recommendation, categorization.
/// </summary>
public class SemanticSearchSpec : PGVectorTestBase
{
    [Fact]
    public async Task SemanticSearch_DocumentSimilarity_FindsRelatedArticles()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Simulate document embeddings (in reality from BERT/OpenAI)
        // We'll use synthetic embeddings with known similarity patterns
        var mlArticle1 = CreateEmbeddingWithBias(384, seed: 100);  // Machine Learning
        var mlArticle2 = CreateEmbeddingWithBias(384, seed: 101);  // Machine Learning (similar)
        var healthArticle = CreateEmbeddingWithBias(384, seed: 200); // Health (different)
        var financeArticle = CreateEmbeddingWithBias(384, seed: 300); // Finance (different)

        await repo.UpsertAsync("ml-basics", mlArticle1, new { Title = "ML Basics", Category = "Tech" });
        await repo.UpsertAsync("ml-advanced", mlArticle2, new { Title = "Advanced ML", Category = "Tech" });
        await repo.UpsertAsync("health-tips", healthArticle, new { Title = "Health Tips", Category = "Health" });
        await repo.UpsertAsync("finance-101", financeArticle, new { Title = "Finance 101", Category = "Finance" });

        // Act - Search with ML article embedding
        var results = await repo.SearchAsync(new VectorQueryOptions(
            Query: mlArticle1,
            TopK: 3
        ));

        // Assert
        results.Results.Should().HaveCount(3);

        // Most similar should be itself
        results.Results[0].Id.Should().Be("ml-basics");
        results.Results[0].Score.Should().BeGreaterThan(0.99);

        // Second most similar should be other ML article
        results.Results[1].Id.Should().Be("ml-advanced");
        results.Results[1].Score.Should().BeGreaterThan(0.8); // Similar domain

        // Least similar in top 3 should be from different category
        results.Results[2].Id.Should().NotBe("ml-basics");
        results.Results[2].Id.Should().NotBe("ml-advanced");
    }

    [Fact]
    public async Task SemanticSearch_HybridFilterAndSimilarity_CombinesMetadataAndVector()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Insert articles across categories with varying similarity
        var techEmbedding = CreateEmbeddingWithBias(384, seed: 100);

        await repo.UpsertAsync("tech-1", techEmbedding, new { Category = "Tech", Popularity = 100 });
        await repo.UpsertAsync("tech-2", CreateEmbeddingWithBias(384, seed: 101), new { Category = "Tech", Popularity = 50 });
        await repo.UpsertAsync("health-1", CreateEmbeddingWithBias(384, seed: 200), new { Category = "Health", Popularity = 200 });
        await repo.UpsertAsync("finance-1", CreateEmbeddingWithBias(384, seed: 300), new { Category = "Finance", Popularity = 150 });

        // Act - Semantic search within Tech category only
        var results = await repo.SearchAsync(new VectorQueryOptions(
            Query: techEmbedding,
            TopK: 10,
            Filter: new { Category = "Tech" }
        ));

        // Assert
        results.Results.Should().HaveCount(2); // Only Tech articles
        results.Results.Should().OnlyContain(r => r.Id.StartsWith("tech-"));

        // Should still be ordered by similarity
        results.Results[0].Score.Should().BeGreaterThanOrEqualTo(results.Results[1].Score);
    }

    [Fact]
    public async Task SemanticSearch_Recommendation_FindsSimilarItems()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Simulate user's reading history (liked articles)
        var userProfile = CreateEmbeddingWithBias(384, seed: 100); // User interests

        // Insert candidate articles
        var similar1 = CreateEmbeddingWithBias(384, seed: 105); // Very similar to user
        var similar2 = CreateEmbeddingWithBias(384, seed: 110); // Somewhat similar
        var dissimilar = CreateEmbeddingWithBias(384, seed: 500); // Very different

        await repo.UpsertAsync("rec-perfect", similar1, new { Type = "recommendation" });
        await repo.UpsertAsync("rec-good", similar2, new { Type = "recommendation" });
        await repo.UpsertAsync("rec-poor", dissimilar, new { Type = "recommendation" });

        // Act - Get recommendations
        var recommendations = await repo.SearchAsync(new VectorQueryOptions(
            Query: userProfile,
            TopK: 2
        ));

        // Assert
        recommendations.Results.Should().HaveCount(2);

        // Best recommendations should be most similar
        recommendations.Results[0].Id.Should().Be("rec-perfect");
        recommendations.Results[1].Id.Should().Be("rec-good");

        // Poor match should be excluded
        recommendations.Results.Should().NotContain(r => r.Id == "rec-poor");
    }

    [Fact]
    public async Task SemanticSearch_Categorization_ClassifiesDocuments()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Create category prototypes (centroids)
        var techCentroid = CreateEmbeddingWithBias(384, seed: 100);
        var healthCentroid = CreateEmbeddingWithBias(384, seed: 200);
        var financeCentroid = CreateEmbeddingWithBias(384, seed: 300);

        await repo.UpsertAsync("category-tech", techCentroid, new { Type = "centroid", Category = "Tech" });
        await repo.UpsertAsync("category-health", healthCentroid, new { Type = "centroid", Category = "Health" });
        await repo.UpsertAsync("category-finance", financeCentroid, new { Type = "centroid", Category = "Finance" });

        // Act - Classify new document (close to health centroid)
        var newDocument = CreateEmbeddingWithBias(384, seed: 205);
        var classification = await repo.SearchAsync(new VectorQueryOptions(
            Query: newDocument,
            TopK: 1,
            Filter: new { Type = "centroid" }
        ));

        // Assert
        classification.Results.Should().HaveCount(1);
        classification.Results[0].Id.Should().Be("category-health"); // Closest category
    }

    [Fact]
    public async Task SemanticSearch_DuplicateDetection_FindsNearDuplicates()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        var originalDoc = CreateEmbeddingWithBias(384, seed: 100);
        var nearDuplicate = CreateEmbeddingWithBias(384, seed: 100); // Same seed = very similar
        var uniqueDoc = CreateEmbeddingWithBias(384, seed: 500);

        await repo.UpsertAsync("doc-original", originalDoc, new { Status = "published" });
        await repo.UpsertAsync("doc-duplicate", nearDuplicate, new { Status = "draft" });
        await repo.UpsertAsync("doc-unique", uniqueDoc, new { Status = "published" });

        // Act - Find duplicates of original
        var duplicates = await repo.SearchAsync(new VectorQueryOptions(
            Query: originalDoc,
            TopK: 3
        ));

        // Assert
        duplicates.Results.Should().HaveCount(3);

        // Top 2 should be original and near-duplicate (high similarity)
        duplicates.Results[0].Score.Should().BeGreaterThan(0.99);
        duplicates.Results[1].Score.Should().BeGreaterThan(0.95); // Near duplicate threshold

        // Unique doc should have lower similarity
        duplicates.Results[2].Score.Should().BeLessThan(0.90);
    }

    [Fact]
    public async Task SemanticSearch_MultilingualSimilarity_WorksAcrossLanguages()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Simulate multilingual embeddings (same semantic space)
        var englishEmbedding = CreateEmbeddingWithBias(384, seed: 100);
        var frenchEmbedding = CreateEmbeddingWithBias(384, seed: 102); // Similar concepts
        var germanEmbedding = CreateEmbeddingWithBias(384, seed: 103);
        var spanishEmbedding = CreateEmbeddingWithBias(384, seed: 104);

        await repo.UpsertAsync("en-article", englishEmbedding, new { Language = "en", Topic = "AI" });
        await repo.UpsertAsync("fr-article", frenchEmbedding, new { Language = "fr", Topic = "AI" });
        await repo.UpsertAsync("de-article", germanEmbedding, new { Language = "de", Topic = "AI" });
        await repo.UpsertAsync("es-article", spanishEmbedding, new { Language = "es", Topic = "Health" });

        // Act - Search with English query
        var results = await repo.SearchAsync(new VectorQueryOptions(
            Query: englishEmbedding,
            TopK: 3
        ));

        // Assert
        results.Results.Should().HaveCount(3);

        // Should find similar concepts across languages
        results.Results.Should().Contain(r => r.Id == "fr-article");
        results.Results.Should().Contain(r => r.Id == "de-article");

        // All results should have decent similarity (same topic)
        results.Results.Should().OnlyContain(r => r.Score > 0.7);
    }

    [Fact]
    public async Task SemanticSearch_ScaleTest_Handles10000Vectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Bulk insert 10,000 vectors
        var items = new List<(string, float[], object?)>();
        for (int i = 0; i < 10000; i++)
        {
            items.Add(($"scale-{i}", CreateEmbeddingWithBias(384, seed: i), new { Batch = i / 1000 }));

            // Batch in groups of 100 for efficient insertion
            if (items.Count == 100)
            {
                await repo.UpsertManyAsync(items);
                items.Clear();
            }
        }

        if (items.Count > 0)
        {
            await repo.UpsertManyAsync(items);
        }

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = await repo.SearchAsync(new VectorQueryOptions(
            Query: CreateEmbeddingWithBias(384, seed: 5000),
            TopK: 10
        ));
        stopwatch.Stop();

        // Assert
        results.Results.Should().HaveCount(10);

        // With HNSW index, search should be sub-linear (< 200ms for 10K vectors)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200);

        output?.WriteLine($"Search latency for 10,000 vectors: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Creates embedding with deterministic bias based on seed.
    /// Embeddings with close seeds will have higher similarity.
    /// </summary>
    private float[] CreateEmbeddingWithBias(int dimension, int seed)
    {
        var random = new Random(seed);
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }

        // Normalize
        var magnitude = 0f;
        for (int i = 0; i < dimension; i++)
        {
            magnitude += embedding[i] * embedding[i];
        }
        magnitude = (float)Math.Sqrt(magnitude);

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] /= magnitude;
        }

        return embedding;
    }

    private readonly Xunit.Abstractions.ITestOutputHelper? output;

    public SemanticSearchSpec(Xunit.Abstractions.ITestOutputHelper? output = null)
    {
        this.output = output;
    }
}
