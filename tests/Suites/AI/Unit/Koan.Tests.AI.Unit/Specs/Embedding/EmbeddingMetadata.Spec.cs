using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using AwesomeAssertions;

namespace Koan.Tests.AI.Unit.Specs.Embedding;

/// <summary>
/// Tests for ADR AI-0020 Phase 3: Enhanced [Embedding] Attribute functionality.
/// Validates embedding text generation, token estimation, truncation, and version-aware signatures.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "3")]
[Trait("Category", "Unit")]
public sealed class EmbeddingMetadataSpec
{
    /// <summary>
    /// Test #24: BuildEmbeddingText_AllStrings_ConcatenatesProperties
    /// </summary>
    [Fact]
    public void AllStrings_policy_concatenates_all_string_properties()
    {
        // Arrange
        var entity = new AllStringsTestEntity
        {
            Title = "Test Title",
            Description = "Test Description",
            Author = "Test Author",
            NumericValue = 42 // Should be ignored
        };

        var metadata = EmbeddingMetadata.Resolve<AllStringsTestEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().Contain("Test Title");
        embeddingText.Should().Contain("Test Description");
        embeddingText.Should().Contain("Test Author");
        embeddingText.Should().NotContain("42", "numeric properties should be excluded");
    }

    /// <summary>
    /// Test #25: BuildEmbeddingText_Explicit_UsesSpecifiedProperties
    /// </summary>
    [Fact]
    public void Explicit_policy_uses_only_specified_properties()
    {
        // Arrange
        var entity = new ExplicitPropertiesEntity
        {
            Title = "Included Title",
            Description = "Included Description",
            InternalNotes = "Should be excluded"
        };

        var metadata = EmbeddingMetadata.Resolve<ExplicitPropertiesEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().Contain("Included Title");
        embeddingText.Should().Contain("Included Description");
        embeddingText.Should().NotContain("Should be excluded");
    }

    /// <summary>
    /// Test #26: BuildEmbeddingText_Template_ReplacesPlaceholders
    /// </summary>
    [Fact]
    public void Template_policy_replaces_placeholders_correctly()
    {
        // Arrange
        var entity = new TemplateEntity
        {
            Title = "Book Title",
            Author = "Author Name",
            Year = 2024
        };

        var metadata = EmbeddingMetadata.Resolve<TemplateEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().Be("Book Title by Author Name (2024)");
    }

    /// <summary>
    /// Test #27: BuildEmbeddingText_FullJson_SerializesEntity
    /// </summary>
    [Fact]
    public void FullJson_policy_serializes_entity_with_depth_limit()
    {
        // Arrange
        var entity = new FullJsonEntity
        {
            Name = "Product Name",
            Price = 99.99m,
            Category = new Category
            {
                Name = "Electronics",
                SubCategory = new Category
                {
                    Name = "Computers" // Depth 2 - should be included
                }
            }
        };

        var metadata = EmbeddingMetadata.Resolve<FullJsonEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().Contain("Product Name");
        embeddingText.Should().Contain("Electronics");
        embeddingText.Should().Contain("99.99");

        // Verify it's valid JSON
        var act = () => System.Text.Json.JsonDocument.Parse(embeddingText);
        act.Should().NotThrow("should generate valid JSON");
    }

    /// <summary>
    /// Test #28: BuildEmbeddingText_FullJson_WithExclusions_OmitsProperties
    /// </summary>
    [Fact]
    public void FullJson_with_exclusions_omits_specified_properties()
    {
        // Arrange
        var entity = new FullJsonWithExclusionsEntity
        {
            Name = "Product",
            Price = 50.00m,
            InternalNotes = "Secret information",
            PasswordHash = "hashed_password"
        };

        var metadata = EmbeddingMetadata.Resolve<FullJsonWithExclusionsEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().Contain("Product");
        embeddingText.Should().Contain("50.00");
        embeddingText.Should().NotContain("Secret information");
        embeddingText.Should().NotContain("hashed_password");
    }

    /// <summary>
    /// Test #29: EstimateTokens_EnglishText_ReturnsReasonableEstimate
    /// </summary>
    [Fact]
    public void EstimateTokens_returns_reasonable_estimate_for_english_text()
    {
        // Arrange
        var text = "This is a sample text with approximately one hundred characters for token estimation testing purposes."; // ~100 chars

        // Act
        var estimatedTokens = EmbeddingMetadata.EstimateTokens(text);

        // Assert (4 chars per token heuristic = ~25 tokens)
        estimatedTokens.Should().BeInRange(20, 30, "estimate should be within ±20% tolerance");
    }

    /// <summary>
    /// Test #30: TruncateToTokenLimit_ExceedsLimit_TruncatesAtWordBoundary
    /// </summary>
    [Fact]
    public void Truncation_preserves_word_boundaries()
    {
        // Arrange
        var entity = new TruncationTestEntity
        {
            Content = "The quick brown fox jumps over the lazy dog. " +
                      "This sentence should be truncated at a word boundary."
        };

        var metadata = EmbeddingMetadata.Resolve<TruncationTestEntity>();

        // Act
        var embeddingText = metadata.BuildEmbeddingText(entity);

        // Assert
        embeddingText.Should().EndWith("...", "truncated text should have ellipsis");
        embeddingText.Should().NotEndWith(" ...", "should truncate at word boundary");

        // Should not end with partial word
        var withoutEllipsis = embeddingText.TrimEnd('.', ' ');
        withoutEllipsis.Split(' ').Last().Should().NotContain(" ", "last word should be complete");
    }

    /// <summary>
    /// Test #32: ComputeSignature_IncludesVersion_VersionChange
    /// </summary>
    [Fact]
    public void Signature_changes_when_version_increments()
    {
        // Arrange
        var entity = new VersionedEntity
        {
            Content = "Same content"
        };

        var metadataV1 = EmbeddingMetadata.Resolve<VersionedEntityV1>();
        var metadataV2 = EmbeddingMetadata.Resolve<VersionedEntityV2>();

        // Act
        var signatureV1 = metadataV1.ComputeSignature(entity);
        var signatureV2 = metadataV2.ComputeSignature(entity);

        // Assert
        signatureV1.Should().NotBe(signatureV2, "different versions should produce different signatures");
    }

    /// <summary>
    /// Test #33: ComputeSignature_SameContent_SameVersion_Stable
    /// </summary>
    [Fact]
    public void Signature_is_stable_for_same_content_and_version()
    {
        // Arrange
        var entity = new VersionedEntity
        {
            Content = "Stable content"
        };

        var metadata = EmbeddingMetadata.Resolve<VersionedEntityV1>();

        // Act
        var signature1 = metadata.ComputeSignature(entity);
        var signature2 = metadata.ComputeSignature(entity);

        // Assert
        signature1.Should().Be(signature2, "identical content should produce identical signatures");
    }

    #region Test Entities

    [Embedding(Policy = EmbeddingPolicy.AllStrings)]
    public class AllStringsTestEntity : Entity<AllStringsTestEntity>
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public int NumericValue { get; set; }
    }

    [Embedding(
        Policy = EmbeddingPolicy.Explicit,
        Properties = new[] { "Title", "Description" })]
    public class ExplicitPropertiesEntity : Entity<ExplicitPropertiesEntity>
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string InternalNotes { get; set; } = "";
    }

    [Embedding(Template = "{Title} by {Author} ({Year})")]
    public class TemplateEntity : Entity<TemplateEntity>
    {
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public int Year { get; set; }
    }

    [Embedding(
        Policy = EmbeddingPolicy.FullJson,
        MaxDepth = 2)]
    public class FullJsonEntity : Entity<FullJsonEntity>
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public Category? Category { get; set; }
    }

    [Embedding(
        Policy = EmbeddingPolicy.FullJson,
        Exclude = new[] { "InternalNotes", "PasswordHash" })]
    public class FullJsonWithExclusionsEntity : Entity<FullJsonWithExclusionsEntity>
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public string InternalNotes { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }

    [Embedding(MaxTokens = 10)] // Very low limit to force truncation
    public class TruncationTestEntity : Entity<TruncationTestEntity>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Version = 1)]
    public class VersionedEntityV1 : Entity<VersionedEntityV1>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Version = 2)]
    public class VersionedEntityV2 : Entity<VersionedEntityV2>
    {
        public string Content { get; set; } = "";
    }

    public class VersionedEntity : Entity<VersionedEntity>
    {
        public string Content { get; set; } = "";
    }

    public class Category
    {
        public string Name { get; set; } = "";
        public Category? SubCategory { get; set; }
    }

    #endregion
}
