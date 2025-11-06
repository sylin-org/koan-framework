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
            Id = "test-1",
            Title = "Test Title",
            Content = "Test content here",
            Tags = new[] { "tag1", "tag2" },
            InternalId = "should-not-appear",
            ViewCount = 42
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert - Check that expected properties are present
        // Note: Order depends on reflection order, so use Contains instead of exact match
        text.Should().Contain("Test Title");
        text.Should().Contain("Test content here");
        text.Should().Contain("tag1, tag2");
        text.Should().Contain("test-1", "Id property is included by AllStrings policy");
        text.Should().NotContain("should-not-appear", "InternalId has [EmbeddingIgnore]");
        text.Should().NotContain("42", "ViewCount is int, not string");
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
            Id = "test-1",
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
        text.Should().NotContain("Tags", "empty arrays should be skipped, not added as property names");
    }

    // ============================================================================
    // EDGE CASE TESTS - Added based on QA Assessment recommendations
    // ============================================================================

    [Fact]
    public void BuildEmbeddingText_UnicodeAndSpecialCharacters_PreservedCorrectly()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = "Test 🚀 Emoji",
            Content = "Unicode: ñ, 中文, 🎨, café",
            Tags = new[] { "日本語", "español" }
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Contain("🚀");
        text.Should().Contain("中文");
        text.Should().Contain("🎨");
        text.Should().Contain("ñ");
        text.Should().Contain("日本語");
        text.Should().Contain("café");
    }

    [Fact]
    public void BuildEmbeddingText_VeryLargeStrings_HandledWithoutException()
    {
        // Arrange - Create very large content (10KB)
        var largeContent = new string('x', 10000);
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = "Large Content Test",
            Content = largeContent,
            Tags = new[] { "large", "test" }
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Length.Should().BeGreaterThan(10000, "large content should be preserved without exception");
    }

    [Fact]
    public void BuildEmbeddingText_StringArrayWithNullElements_SkipsNulls()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = "Test",
            Content = "Content",
            Tags = new[] { "valid", null!, "also-valid" }
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Contain("valid");
        text.Should().Contain("also-valid");
        text.Should().NotContain("null");
    }

    [Fact]
    public void BuildEmbeddingText_AllPropertiesNull_OnlyIncludesId()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = null!,
            Content = null!,
            Tags = null!
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Be("test-1", "when all other properties are null, only Id should be included");
    }

    [Fact]
    public void BuildEmbeddingText_AllPropertiesWhitespace_OnlyIncludesId()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = "   ",
            Content = "\t\n",
            Tags = new[] { "  ", "\t" }
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var text = metadata.BuildEmbeddingText(doc);

        // Assert
        text.Should().Be("test-1", "whitespace-only properties should be skipped, leaving only Id");
    }

    [Fact]
    public void EmbeddingMetadata_ExplicitPolicy_WithoutPropertiesOrTemplate_ThrowsInvalidOperationException()
    {
        // Arrange & Act
        Action act = () => EmbeddingMetadata.Get<TestExplicitEntityWithoutProperties>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*uses EmbeddingPolicy.Explicit*")
            .WithMessage("*does not specify Properties or Template*");
    }

    [Fact]
    public void ComputeSignature_EmptyContent_ProducesValidHash()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "test-1",
            Title = null!,
            Content = null!,
            Tags = null!
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var signature = metadata.ComputeSignature(doc);

        // Assert
        signature.Should().NotBeNullOrEmpty();
        signature.Should().HaveLength(64, "SHA256 hash should be 64 hex characters");
    }

    [Fact]
    public void ComputeSignature_SpecialCharacters_ProducesConsistentHash()
    {
        // Arrange
        var doc1 = new TestDocument
        {
            Id = "test-1",
            Title = "Special: <>&\"'",
            Content = "Symbols: !@#$%^&*()"
        };
        var doc2 = new TestDocument
        {
            Id = "test-1",
            Title = "Special: <>&\"'",
            Content = "Symbols: !@#$%^&*()"
        };
        var metadata = EmbeddingMetadata.Get<TestDocument>();

        // Act
        var sig1 = metadata.ComputeSignature(doc1);
        var sig2 = metadata.ComputeSignature(doc2);

        // Assert
        sig1.Should().Be(sig2, "identical content with special chars should produce same hash");
    }

    [Fact]
    public async Task EmbeddingMetadata_ConcurrentAccess_ReturnsSameCachedInstance()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => EmbeddingMetadata.Get<TestDocument>()))
            .ToArray();

        await Task.WhenAll(tasks);
        var results = tasks.Select(t => t.Result).ToList();

        // Assert - Verify cache returns identical instance (reference equality) for thread safety
        results.Should().OnlyContain(m => ReferenceEquals(m, results[0]),
            "all concurrent calls should return the exact same cached instance");
    }

    // ============================================================================
    // TEMPLATE EDGE CASE TESTS - Critical for production readiness
    // ============================================================================

    [Fact]
    public void BuildEmbeddingText_TemplateWithNonExistentProperty_LeavesPlaceholder()
    {
        // Arrange
        var entity = new TestTemplateWithBadProperty
        {
            Title = "Test Title",
            Content = "Test Content"
        };
        var metadata = EmbeddingMetadata.Get<TestTemplateWithBadProperty>();

        // Act
        var text = metadata.BuildEmbeddingText(entity);

        // Assert
        text.Should().Be("Title: Test Title\nMissing: {NonExistentProp}\nContent: Test Content",
            "non-existent properties leave placeholder in output - this helps identify configuration errors");
        text.Should().Contain("{NonExistentProp}", "placeholder indicates missing property");
    }

    [Fact]
    public void BuildEmbeddingText_TemplateWithPartiallyValidProperties_ProcessesValidOnes()
    {
        // Arrange
        var entity = new TestTemplateMixedProperties
        {
            ValidProp = "Valid Value",
            AnotherValid = "Another Value"
        };
        var metadata = EmbeddingMetadata.Get<TestTemplateMixedProperties>();

        // Act
        var text = metadata.BuildEmbeddingText(entity);

        // Assert
        text.Should().Be("Valid Value - {InvalidProp} - Another Value",
            "valid properties replaced, invalid properties left as placeholders");
        text.Should().Contain("Valid Value", "valid properties should be processed");
        text.Should().Contain("Another Value", "valid properties should be processed");
        text.Should().Contain("{InvalidProp}", "invalid property left as placeholder for debugging");
    }

    [Fact]
    public void BuildEmbeddingText_TemplateWithNullPropertyValue_ReplacesWithEmptyString()
    {
        // Arrange
        var entity = new TestTemplateWithNullable
        {
            RequiredField = "Present",
            OptionalField = null
        };
        var metadata = EmbeddingMetadata.Get<TestTemplateWithNullable>();

        // Act
        var text = metadata.BuildEmbeddingText(entity);

        // Assert
        text.Should().Be("Required: Present, Optional: ",
            "null values in template should be replaced with empty string");
    }

    [Fact]
    public void EmbeddingMetadata_AllPublicPolicy_IncludesAllPublicProperties()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestAllPublicEntity>();

        // Assert
        metadata.Policy.Should().Be(EmbeddingPolicy.AllPublic);
        metadata.Properties.Should().Contain("StringProp", "AllPublic should include string properties");
        metadata.Properties.Should().Contain("IntProp", "AllPublic should include int properties");
        metadata.Properties.Should().Contain("BoolProp", "AllPublic should include bool properties");
        metadata.Properties.Should().NotContain("IgnoredProp", "[EmbeddingIgnore] should exclude property");
    }

    [Fact]
    public void BuildEmbeddingText_AllPublicPolicy_OnlyIncludesStringTypes()
    {
        // Arrange
        var entity = new TestAllPublicEntity
        {
            Id = "test-id",
            StringProp = "text",
            IntProp = 42,
            BoolProp = true
        };
        var metadata = EmbeddingMetadata.Get<TestAllPublicEntity>();

        // Act
        var text = metadata.BuildEmbeddingText(entity);

        // Assert
        text.Should().Contain("text", "string properties should be included");
        text.Should().Contain("test-id", "Id is a string property");
        text.Should().NotContain("42", "non-string properties are not included in non-template mode");
        text.Should().NotContain("True", "non-string properties are not included in non-template mode");
    }

    [Fact]
    public void BuildEmbeddingText_EmptyPropertiesArray_FallsBackToPolicy()
    {
        // Arrange - Entity with Properties = new string[0]
        var metadata = EmbeddingMetadata.Get<TestEmptyPropertiesArray>();

        var entity = new TestEmptyPropertiesArray
        {
            Id = "test-id",
            Field1 = "Field 1 Value",
            Field2 = "Field 2 Value"
        };

        // Act
        var text = metadata.BuildEmbeddingText(entity);

        // Assert
        // Empty properties array is treated as "not specified" and falls back to AllStrings policy
        text.Should().Contain("Field 1 Value", "empty properties array falls back to AllStrings policy");
        text.Should().Contain("Field 2 Value", "empty properties array falls back to AllStrings policy");
        text.Should().Contain("test-id", "Id is included by AllStrings policy");
    }
}

/// <summary>
/// Test entity with Explicit policy but no Properties or Template specified
/// Used to test error handling
/// </summary>
[Koan.Data.AI.Attributes.Embedding(Policy = Koan.Data.AI.Attributes.EmbeddingPolicy.Explicit)]
public class TestExplicitEntityWithoutProperties : Koan.Data.Core.Model.Entity<TestExplicitEntityWithoutProperties>
{
    public string Field { get; set; } = "";
}
