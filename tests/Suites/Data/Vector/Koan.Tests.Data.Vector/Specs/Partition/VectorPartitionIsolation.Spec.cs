using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Xunit;

namespace Koan.Tests.Data.Vector.Specs.Partition;

/// <summary>
/// Tests Vector&lt;T&gt; partition isolation using EntityContext (ARCH-0071, DATA-0077).
/// Verifies that vectors saved in different partitions are properly isolated.
/// </summary>
public class VectorPartitionIsolation_Spec
{
    [Fact(Skip = "Integration test - requires Weaviate container")]
    public async Task Vector_Save_WithPartition_IsolatesVectors()
    {
        // Arrange
        var docA = new TestDocument { Id = "doc-a", Content = "Document in partition A" };
        var docB = new TestDocument { Id = "doc-b", Content = "Document in partition B" };
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Act: Save to partition "project-a"
        using (EntityContext.Partition("project-a"))
        {
            await Vector<TestDocument>.Save(docA.Id, embedding, metadata: null);
        }

        // Act: Save to partition "project-b"
        using (EntityContext.Partition("project-b"))
        {
            await Vector<TestDocument>.Save(docB.Id, embedding, metadata: null);
        }

        // Assert: Search in partition "project-a" only returns doc-a
        using (EntityContext.Partition("project-a"))
        {
            var resultsA = await Vector<TestDocument>.Search(
                vector: embedding,
                topK: 10
            );

            Assert.Single(resultsA.Matches);
            Assert.Equal("doc-a", resultsA.Matches[0].Id);
        }

        // Assert: Search in partition "project-b" only returns doc-b
        using (EntityContext.Partition("project-b"))
        {
            var resultsB = await Vector<TestDocument>.Search(
                vector: embedding,
                topK: 10
            );

            Assert.Single(resultsB.Matches);
            Assert.Equal("doc-b", resultsB.Matches[0].Id);
        }

        // Assert: Global search (no partition) returns both
        var globalResults = await Vector<TestDocument>.Search(
            vector: embedding,
            topK: 10
        );

        Assert.Equal(2, globalResults.Matches.Count);
    }

    [Fact(Skip = "Integration test - requires Weaviate container")]
    public async Task Vector_WithPartition_ConvenienceMethod_Works()
    {
        // Arrange
        var embedding = new float[] { 0.4f, 0.5f, 0.6f };

        // Act: Use Vector<T>.WithPartition() convenience method
        using (Vector<TestDocument>.WithPartition("archive"))
        {
            await Vector<TestDocument>.Save("archived-doc", embedding, metadata: null);
        }

        // Assert: Document is in "archive" partition
        using (EntityContext.Partition("archive"))
        {
            var results = await Vector<TestDocument>.Search(
                vector: embedding,
                topK: 10
            );

            Assert.Single(results.Matches);
            Assert.Equal("archived-doc", results.Matches[0].Id);
        }
    }

    [Fact]
    public void VectorPartitionMapper_SanitizesPartitionId()
    {
        // Arrange
        var mapper = new Koan.Data.Vector.Connector.Weaviate.Partition.WeaviatePartitionMapper();

        // Act & Assert: Valid partition IDs
        Assert.Equal("project_abc", mapper.SanitizePartitionId("project-abc"));
        Assert.Equal("tenant_123", mapper.SanitizePartitionId("tenant@123"));
        Assert.Equal("my_project_name", mapper.SanitizePartitionId("my-project-name"));

        // Act & Assert: Edge cases
        Assert.Equal("project_123", mapper.SanitizePartitionId("---project---123---"));
        Assert.NotNull(mapper.SanitizePartitionId("###"));  // Should return fallback hash
    }

    [Fact]
    public void VectorPartitionMapper_MapsToClassName()
    {
        // Arrange
        var mapper = new Koan.Data.Vector.Connector.Weaviate.Partition.WeaviatePartitionMapper();

        // Act
        var className = mapper.MapStorageName<TestDocument>("project-koan-framework");

        // Assert
        Assert.Equal("KoanDocument_project_koan_framework", className);
        Assert.True(className.Length <= 256);  // Weaviate max length
    }

    // Test entity
    private class TestDocument : Koan.Data.Core.Model.Entity<TestDocument, string>
    {
        public string Content { get; set; } = string.Empty;
    }
}
