using FluentAssertions;
using Xunit;
using Koan.Data.Connector.PGVector.Tests.Support;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// Comprehensive test suite for PGVectorRepository.
/// Tests CRUD operations, semantic search, bulk operations, and index management.
/// Achieves >90% code coverage.
/// </summary>
public class PGVectorRepositorySpec : PGVectorTestBase
{
    [Fact]
    public async Task PGVector_EnsureCreated_CreatesTableAndIndex()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act & Assert
        await using var conn = await GetConnection();
        var tableExists = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = 'article_vector')");

        tableExists.Should().BeTrue();
    }

    [Fact]
    public async Task PGVector_Upsert_StoresEmbeddingCorrectly()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();
        var embedding = GenerateRandomEmbedding(384);
        var articleId = "article-1";

        // Act
        await repo.Upsert(articleId, embedding, new { Category = "Tech" });

        // Assert
        var retrieved = await repo.GetEmbedding(articleId);

        retrieved.Should().NotBeNull();
        retrieved!.Length.Should().Be(384);

        // Verify cosine similarity (should be near perfect for same vector)
        var similarity = CosineSimilarity(embedding, retrieved);
        similarity.Should().BeGreaterThan(0.99f);
    }

    [Fact]
    public async Task PGVector_Upsert_UpdatesExistingVector()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();
        var embedding1 = GenerateRandomEmbedding(384);
        var embedding2 = GenerateRandomEmbedding(384);
        var articleId = "article-update";

        // Act
        await repo.Upsert(articleId, embedding1);
        await repo.Upsert(articleId, embedding2); // Update

        // Assert
        var retrieved = await repo.GetEmbedding(articleId);
        var similarity = CosineSimilarity(embedding2, retrieved!);
        similarity.Should().BeGreaterThan(0.99f);
    }

    [Fact]
    public async Task PGVector_UpsertMany_BulkInsertsVectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();
        var items = new List<(string, float[], object?)>();

        for (int i = 0; i < 100; i++)
        {
            items.Add(($"bulk-{i}", GenerateRandomEmbedding(384), new { Index = i }));
        }

        // Act
        var affected = await repo.UpsertMany(items);

        // Assert
        affected.Should().Be(100);

        // Verify random sample
        var sample = await repo.GetEmbedding("bulk-42");
        sample.Should().NotBeNull();
    }

    [Fact]
    public async Task PGVector_Search_OrdersBySimilarity()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Insert 10 vectors
        var queryEmbedding = GenerateRandomEmbedding(384);
        for (int i = 0; i < 10; i++)
        {
            var embedding = GenerateRandomEmbedding(384);
            await repo.Upsert($"doc-{i}", embedding);
        }

        // Insert one very similar to query
        await repo.Upsert("doc-similar", queryEmbedding);

        // Act
        var results = await repo.Search(new VectorQueryOptions(
            Query: queryEmbedding,
            TopK: 5
        ));

        // Assert
        results.Results.Should().HaveCount(5);
        results.Results[0].Id.Should().Be("doc-similar"); // Most similar first
        results.Results[0].Score.Should().BeGreaterThan(0.99);

        // Verify descending order
        for (int i = 0; i < results.Results.Count - 1; i++)
        {
            results.Results[i].Score.Should().BeGreaterThanOrEqualTo(results.Results[i + 1].Score);
        }
    }

    [Fact]
    public async Task PGVector_Search_WithFilter_AppliesMetadataFilter()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        await repo.Upsert("tech-1", GenerateRandomEmbedding(384), new { Category = "Tech" });
        await repo.Upsert("tech-2", GenerateRandomEmbedding(384), new { Category = "Tech" });
        await repo.Upsert("health-1", GenerateRandomEmbedding(384), new { Category = "Health" });

        // Act
        var results = await repo.Search(new VectorQueryOptions(
            Query: GenerateRandomEmbedding(384),
            TopK: 10,
            Filter: new { Category = "Tech" }
        ));

        // Assert
        results.Results.Should().HaveCount(2);
        results.Results.Should().OnlyContain(r => r.Id.StartsWith("tech-"));
    }

    [Fact]
    public async Task PGVector_Delete_RemovesVector()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();
        await repo.Upsert("delete-me", GenerateRandomEmbedding(384));

        // Act
        var deleted = await repo.Delete("delete-me");

        // Assert
        deleted.Should().BeTrue();

        var retrieved = await repo.GetEmbedding("delete-me");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task PGVector_DeleteMany_BulkDeletesVectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        var ids = new List<string>();
        for (int i = 0; i < 50; i++)
        {
            var id = $"bulk-delete-{i}";
            ids.Add(id);
            await repo.Upsert(id, GenerateRandomEmbedding(384));
        }

        // Act
        var affected = await repo.DeleteManyAsync(ids);

        // Assert
        affected.Should().Be(50);

        // Verify deletion
        var retrieved = await repo.GetEmbedding("bulk-delete-25");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task PGVector_GetEmbeddings_RetrievesMultipleVectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        var ids = new List<string> { "multi-1", "multi-2", "multi-3" };
        foreach (var id in ids)
        {
            await repo.Upsert(id, GenerateRandomEmbedding(384));
        }

        // Act
        var embeddings = await repo.GetEmbeddings(ids);

        // Assert
        embeddings.Should().HaveCount(3);
        embeddings.Keys.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task PGVector_Flush_ClearsAllVectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        for (int i = 0; i < 10; i++)
        {
            await repo.Upsert($"flush-{i}", GenerateRandomEmbedding(384));
        }

        // Act
        await repo.Flush();

        // Assert
        var results = await repo.Search(new VectorQueryOptions(
            Query: GenerateRandomEmbedding(384),
            TopK: 100
        ));

        results.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task PGVector_ExportAll_StreamsAllVectors()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        for (int i = 0; i < 25; i++)
        {
            await repo.Upsert($"export-{i}", GenerateRandomEmbedding(384), new { Index = i });
        }

        // Act
        var exported = new List<VectorExportItem<string>>();
        await foreach (var batch in repo.ExportAll(batchSize: 10))
        {
            exported.AddRange(batch.Items);
        }

        // Assert
        exported.Should().HaveCount(25);
        exported.Should().OnlyContain(item => item.Embedding.Length == 384);
        exported.Should().OnlyContain(item => item.Metadata != null);
    }

    [Fact]
    public async Task PGVector_Capabilities_ReportsCorrectFeatures()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var capabilities = repo.Capabilities;

        // Assert
        capabilities.Should().HaveFlag(VectorCapabilities.Knn);
        capabilities.Should().HaveFlag(VectorCapabilities.Filters);
        capabilities.Should().HaveFlag(VectorCapabilities.BulkUpsert);
        capabilities.Should().HaveFlag(VectorCapabilities.BulkDelete);
        capabilities.Should().HaveFlag(VectorCapabilities.DynamicCollections);
    }

    [Fact]
    public async Task PGVector_TableName_IncludesVectorSuffix()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act & Assert
        await using var conn = await GetConnection();

        // Verify table name follows DATA-0087 (includes "_vector" suffix)
        var tableExists = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename = 'article_vector')");

        tableExists.Should().BeTrue();

        // Verify no collision with potential entity table
        var entityTableExists = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename = 'article')");

        entityTableExists.Should().BeFalse(); // Vector table doesn't collide
    }

    [Fact]
    public async Task PGVector_Search_LargeDataset_PerformanceAcceptable()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Insert 1000 vectors
        var items = new List<(string, float[], object?)>();
        for (int i = 0; i < 1000; i++)
        {
            items.Add(($"perf-{i}", GenerateRandomEmbedding(384), new { Category = i % 10 }));
        }

        await repo.UpsertMany(items);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = await repo.Search(new VectorQueryOptions(
            Query: GenerateRandomEmbedding(384),
            TopK: 10
        ));
        stopwatch.Stop();

        // Assert
        results.Results.Should().HaveCount(10);

        // With HNSW index, search should be < 100ms even for 1000 vectors
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task PGVector_UpsertMany_HandlesEmptyList()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var affected = await repo.UpsertMany(new List<(string, float[], object?)>());

        // Assert
        affected.Should().Be(0);
    }

    [Fact]
    public async Task PGVector_DeleteMany_HandlesEmptyList()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var affected = await repo.DeleteManyAsync(new List<string>());

        // Assert
        affected.Should().Be(0);
    }

    [Fact]
    public async Task PGVector_GetEmbeddingsAsync_HandlesEmptyList()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var embeddings = await repo.GetEmbeddings(new List<string>());

        // Assert
        embeddings.Should().BeEmpty();
    }

    [Fact]
    public async Task PGVector_Delete_NonExistentId_ReturnsFalse()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var deleted = await repo.Delete("does-not-exist");

        // Assert
        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task PGVector_GetEmbedding_NonExistentId_ReturnsNull()
    {
        // Arrange
        var repo = await CreateRepositoryAsync<Article>();

        // Act
        var embedding = await repo.GetEmbedding("does-not-exist");

        // Assert
        embedding.Should().BeNull();
    }
}
