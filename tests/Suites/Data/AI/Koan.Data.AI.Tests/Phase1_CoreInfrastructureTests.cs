using FluentAssertions;
using Koan.Data.AI.Attributes;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Tests for Phase 1: Core Infrastructure
/// - EmbeddingAttribute configuration
/// - EmbeddingMetadata parsing
/// - Content signature computation
/// - Template parsing
/// </summary>
public class Phase1_CoreInfrastructureTests
{
    [Fact]
    public void EmbeddingMetadata_AllStringsPolicy_IncludesStringPropertiesOnly()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Assert
        metadata.Policy.Should().Be(EmbeddingPolicy.AllStrings);
        metadata.Properties.Should().Contain("Title");
        metadata.Properties.Should().Contain("Content");
        metadata.Properties.Should().Contain("Tags");
        metadata.Properties.Should().NotContain("InternalId"); // Has [EmbeddingIgnore]
        metadata.Properties.Should().NotContain("ViewCount"); // Not a string
    }

    [Fact]
    public void EmbeddingMetadata_ExplicitProperties_IncludesOnlySpecified()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestArticle>();

        // Assert
        metadata.Properties.Should().HaveCount(2);
        metadata.Properties.Should().Contain("Title");
        metadata.Properties.Should().Contain("Summary");
        metadata.Properties.Should().NotContain("InternalNotes");
    }

    [Fact]
    public void EmbeddingMetadata_TemplateMode_ParsesTemplate()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestPost>();

        // Assert
        metadata.Template.Should().Be("Title: {Title}\nAuthor: {Author}\nContent: {Content}");
        metadata.Properties.Should().Contain("Title");
        metadata.Properties.Should().Contain("Author");
        metadata.Properties.Should().Contain("Content");
    }

    [Fact]
    public void EmbeddingMetadata_AsyncConfiguration_IsPreserved()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestAsyncDocument>();

        // Assert
        metadata.Async.Should().BeTrue();
        metadata.RateLimitPerMinute.Should().Be(30);
    }

    [Fact]
    public void BuildEmbeddingText_AllStringsPolicy_ConcatenatesStringProperties()
    {
        // Arrange
        var doc = new TestDocument
        {
            Title = "Test Title",
            Content = "Test content here",
            Tags = new[] { "tag1", "tag2" },
            InternalId = "should-not-appear",
            ViewCount = 42
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Contain("Test Title");
        text.Should().Contain("Test content here");
        text.Should().Contain("tag1");
        text.Should().Contain("tag2");
        text.Should().NotContain("should-not-appear");
        text.Should().NotContain("42");
    }

    [Fact]
    public void BuildEmbeddingText_TemplateMode_AppliesTemplate()
    {
        // Arrange
        var post = new TestPost
        {
            Title = "My Post",
            Author = "John Doe",
            Content = "Post content"
        };
        var metadata = EmbeddingMetadata.Get<TestPost>();

        // Act
        var text = metadata.BuildEmbeddingText(post);

        // Assert
        text.Should().Be("Title: My Post\nAuthor: John Doe\nContent: Post content");
    }

    [Fact]
    public void ComputeSignature_SameContent_ProducesSameHash()
    {
        // Arrange
        var doc1 = new TestDocument
        {
            Id = "test-1",
            Title = "Same",
            Content = "Content"
        };
        var doc2 = new TestDocument
        {
            Id = "test-1",
            Title = "Same",
            Content = "Content"
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var sig1 = metadata.ComputeSignature(doc1);
        var sig2 = metadata.ComputeSignature(doc2);

        // Assert
        sig1.Should().Be(sig2);
        sig1.Should().HaveLength(64); // SHA256 = 32 bytes = 64 hex chars
    }

    [Fact]
    public void ComputeSignature_DifferentContent_ProducesDifferentHash()
    {
        // Arrange
        var doc1 = new TestDocument
        {
            Id = "test-1",
            Title = "First",
            Content = "Content"
        };
        var doc2 = new TestDocument
        {
            Id = "test-2",
            Title = "Second",
            Content = "Content"
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var sig1 = metadata.ComputeSignature(doc1);
        var sig2 = metadata.ComputeSignature(doc2);

        // Assert
        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void EmbeddingMetadata_CachesResults()
    {
        // Arrange & Act
        var metadata1 = EmbeddingMetadata.Get<TestDocument>();
        var metadata2 = EmbeddingMetadata.Get<TestDocument>();

        // Assert
        metadata1.Should().BeSameAs(metadata2); // Same instance from cache
    }

    [Fact]
    public void EmbeddingMetadata_ExplicitPolicy_IncludesOnlySpecifiedProperties()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestExplicitEntity>();

        // Assert
        metadata.Policy.Should().Be(EmbeddingPolicy.Explicit);
        metadata.Properties.Should().HaveCount(1);
        metadata.Properties.Should().Contain("PublicField");
        metadata.Properties.Should().NotContain("PrivateField");
    }

    [Fact]
    public void BuildEmbeddingText_NullValues_HandledGracefully()
    {
        // Arrange
        var doc = new TestDocument
        {
            Title = "Title",
            Content = null!, // Null string
            Tags = null! // Null array
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Contain("Title");
        text.Should().NotContain("null");
    }

    [Fact]
    public void BuildEmbeddingText_EmptyArrays_HandledGracefully()
    {
        // Arrange
        var doc = new TestDocument
        {
            Title = "Title",
            Content = "Content",
            Tags = Array.Empty<string>()
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Contain("Title");
        text.Should().Contain("Content");
    }
}
