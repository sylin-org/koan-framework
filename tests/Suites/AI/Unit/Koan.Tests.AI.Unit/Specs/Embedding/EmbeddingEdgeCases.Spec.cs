using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using AwesomeAssertions;
using System.Text.Json;

namespace Koan.Tests.AI.Unit.Specs.Embedding;

/// <summary>
/// Edge case tests for EmbeddingMetadata (ADR AI-0020 Phase 3).
/// Validates behavior with null values, empty data, extreme inputs, and error conditions.
/// Addresses QA gap: "No edge case coverage for Phase 3"
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "3")]
[Trait("Category", "Unit")]
[Trait("Quality", "EdgeCases")]
public sealed class EmbeddingEdgeCasesSpec
{
    /// <summary>
    /// Test: Null property values are handled gracefully (skipped).
    /// </summary>
    [Fact]
    public void BuildEmbeddingText_null_properties_are_skipped()
    {
        var metadata = EmbeddingMetadata.Resolve<AllStringsTestEntity>();

        var entity = new AllStringsTestEntity
        {
            Title = null!,
            Description = "Only description"
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().NotContain("null");
        embeddingText.Should().Contain("Only description");
    }

    /// <summary>
    /// Test: All properties null produces empty string.
    /// </summary>
    [Fact]
    public void BuildEmbeddingText_all_null_properties_returns_empty_string()
    {
        var metadata = EmbeddingMetadata.Resolve<AllStringsTestEntity>();

        var entity = new AllStringsTestEntity
        {
            Title = null!,
            Description = null!
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().BeEmpty("all properties are null");
    }

    /// <summary>
    /// Test: Template with missing properties replaces with empty string.
    /// </summary>
    [Fact]
    public void RenderTemplate_missing_property_replaces_with_empty()
    {
        var metadata = EmbeddingMetadata.Resolve<TemplateEntity>();

        var entity = new TemplateEntity
        {
            Title = "Test Title",
            Content = null!
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().Contain("Test Title");
        embeddingText.Should().NotContain("{Content}");
        embeddingText.Should().NotContain("null");
    }

    /// <summary>
    /// Test: Very long word without spaces truncates mid-word.
    /// </summary>
    [Fact]
    public void Truncation_very_long_word_truncates_mid_word()
    {
        var metadata = EmbeddingMetadata.Resolve<TruncationTestEntity>();

        // Create a 10,000-character word (no spaces)
        var veryLongWord = new string('a', 10000);

        var entity = new TruncationTestEntity
        {
            Content = veryLongWord
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        // MaxTokens=100 means ~400 chars max
        embeddingText.Length.Should().BeLessThan(500, "text should be truncated");
        embeddingText.Should().EndWith("...", "ellipsis should be appended");
    }

    /// <summary>
    /// Test: MaxTokens=1 produces minimal text.
    /// </summary>
    [Fact]
    public void Truncation_max_tokens_one_produces_ellipsis()
    {
        var entity = new MaxTokensOneEntity
        {
            Content = "This is a very long text that should be truncated to almost nothing"
        };

        var metadata = EmbeddingMetadata.Resolve<MaxTokensOneEntity>();
        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().Be("...", "MaxTokens=1 with long text should produce just ellipsis");
    }

    /// <summary>
    /// Test: MaxTokens=0 does not truncate.
    /// </summary>
    [Fact]
    public void Truncation_max_tokens_zero_no_truncation()
    {
        var entity = new MaxTokensZeroEntity
        {
            Content = new string('x', 10000)
        };

        var metadata = EmbeddingMetadata.Resolve<MaxTokensZeroEntity>();
        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Length.Should().Be(10000, "MaxTokens=0 should not truncate");
    }

    /// <summary>
    /// Test: String array with all null/empty values produces empty string.
    /// </summary>
    [Fact]
    public void BuildEmbeddingText_string_array_all_empty_produces_empty()
    {
        var metadata = EmbeddingMetadata.Resolve<StringArrayEntity>();

        var entity = new StringArrayEntity
        {
            Tags = new[] { "", "   ", null! }
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().BeEmpty("all array values are null/whitespace");
    }

    /// <summary>
    /// Test: FullJson with MaxDepth=1 limits nesting.
    /// </summary>
    [Fact]
    public void FullJson_max_depth_one_limits_nesting()
    {
        var metadata = EmbeddingMetadata.Resolve<MaxDepthOneEntity>();

        var entity = new MaxDepthOneEntity
        {
            Title = "Root",
            Nested = new NestedObject
            {
                Name = "Level 1",
                DeepNested = new DeepNestedObject
                {
                    Value = "Level 2 - Should be excluded"
                }
            }
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);
        var json = JsonDocument.Parse(embeddingText);

        json.RootElement.GetProperty("Title").GetString().Should().Be("Root");
        json.RootElement.GetProperty("Nested").GetProperty("Name").GetString().Should().Be("Level 1");

        // DeepNested should be null due to depth limit
        json.RootElement.GetProperty("Nested").TryGetProperty("DeepNested", out var deepNested).Should().BeTrue();
        deepNested.ValueKind.Should().Be(JsonValueKind.Null, "depth limit should truncate deep nesting");
    }

    /// <summary>
    /// Test: FullJson with circular references handled via ReferenceHandler.IgnoreCycles.
    /// </summary>
    [Fact]
    public void FullJson_circular_reference_handled_gracefully()
    {
        var metadata = EmbeddingMetadata.Resolve<CircularReferenceEntity>();

        var parent = new CircularReferenceEntity
        {
            Title = "Parent"
        };

        var child = new CircularReferenceEntity
        {
            Title = "Child",
            Parent = parent
        };

        parent.Children = new[] { child };

        // Should not throw, circular reference ignored
        var act = () => metadata.BuildEmbeddingText(parent);
        act.Should().NotThrow("circular references should be handled via IgnoreCycles");

        var embeddingText = metadata.BuildEmbeddingText(parent);
        embeddingText.Should().Contain("Parent");
        embeddingText.Should().Contain("Child");
    }

    /// <summary>
    /// Test: Exclusion list removes properties from all policies.
    /// </summary>
    [Fact]
    public void Exclude_removes_properties_from_all_policies()
    {
        var metadata = EmbeddingMetadata.Resolve<ExclusionEntity>();

        var entity = new ExclusionEntity
        {
            PublicData = "Should be included",
            SecretData = "Should be excluded"
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().Contain("Should be included");
        embeddingText.Should().NotContain("Should be excluded");
        embeddingText.Should().NotContain("SecretData");
    }

    /// <summary>
    /// Test: FullJson exclusion drops the property at the contract level (Newtonsoft ExclusionContractResolver).
    /// </summary>
    [Fact]
    public void FullJson_exclude_removes_property_via_contract_resolver()
    {
        var metadata = EmbeddingMetadata.Resolve<FullJsonExclusionEntity>();

        var entity = new FullJsonExclusionEntity
        {
            PublicData = "Should be included",
            SecretData = "Should be excluded"
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().StartWith("{", "FullJson policy serializes the entity as JSON");
        embeddingText.Should().Contain("PublicData").And.Contain("Should be included");
        embeddingText.Should().NotContain("SecretData", "the excluded property must be dropped from the JSON");
        embeddingText.Should().NotContain("Should be excluded");
    }

    /// <summary>
    /// Test: Empty template produces empty string.
    /// </summary>
    [Fact]
    public void RenderTemplate_empty_template_produces_empty_string()
    {
        var metadata = EmbeddingMetadata.Resolve<EmptyTemplateEntity>();

        var entity = new EmptyTemplateEntity
        {
            Title = "This should not appear"
        };

        var embeddingText = metadata.BuildEmbeddingText(entity);

        embeddingText.Should().BeEmpty("template is empty string");
    }

    /// <summary>
    /// Test: Signature changes when content changes.
    /// </summary>
    [Fact]
    public void ComputeSignature_changes_when_content_changes()
    {
        var metadata = EmbeddingMetadata.Resolve<AllStringsTestEntity>();

        var entity1 = new AllStringsTestEntity { Title = "Version 1", Description = "Content A" };
        var entity2 = new AllStringsTestEntity { Title = "Version 1", Description = "Content B" };

        var signature1 = metadata.ComputeSignature(entity1);
        var signature2 = metadata.ComputeSignature(entity2);

        signature1.Should().NotBe(signature2, "different content should produce different signatures");
    }

    /// <summary>
    /// Test: Signature includes version number.
    /// </summary>
    [Fact]
    public void ComputeSignature_includes_version_number()
    {
        var metadata1 = EmbeddingMetadata.Resolve<VersionedEntity>();
        var metadata2 = EmbeddingMetadata.Resolve<VersionedEntityV2>();

        var entity1 = new VersionedEntity { Content = "Same content" };
        var entity2 = new VersionedEntityV2 { Content = "Same content" };

        var signature1 = metadata1.ComputeSignature(entity1);
        var signature2 = metadata2.ComputeSignature(entity2);

        signature1.Should().NotBe(signature2, "different versions should produce different signatures even with same content");
    }

    /// <summary>
    /// Test: Signature is deterministic (same input always produces same output).
    /// </summary>
    [Fact]
    public void ComputeSignature_is_deterministic()
    {
        var metadata = EmbeddingMetadata.Resolve<AllStringsTestEntity>();

        var entity = new AllStringsTestEntity { Title = "Test", Description = "Deterministic" };

        var signature1 = metadata.ComputeSignature(entity);
        var signature2 = metadata.ComputeSignature(entity);

        signature1.Should().Be(signature2, "same entity should always produce same signature");
    }

    /// <summary>
    /// Test: EstimateTokens returns reasonable values.
    /// </summary>
    [Fact]
    public void EstimateTokens_returns_reasonable_values()
    {
        // ~4 chars per token
        var text400 = new string('x', 400);
        var tokens = EmbeddingMetadata.EstimateTokens(text400);

        tokens.Should().BeInRange(90, 110, "400 chars should be ~100 tokens");
    }

    #region Test Entities

    [Embedding(Policy = EmbeddingPolicy.AllStrings)]
    public class AllStringsTestEntity : Entity<AllStringsTestEntity>
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [Embedding(Template = "Title: {Title}\n\nContent: {Content}")]
    public class TemplateEntity : Entity<TemplateEntity>
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, MaxTokens = 100)]
    public class TruncationTestEntity : Entity<TruncationTestEntity>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, MaxTokens = 1)]
    public class MaxTokensOneEntity : Entity<MaxTokensOneEntity>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, MaxTokens = 0)]
    public class MaxTokensZeroEntity : Entity<MaxTokensZeroEntity>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings)]
    public class StringArrayEntity : Entity<StringArrayEntity>
    {
        public string[] Tags { get; set; } = [];
    }

    [Embedding(Policy = EmbeddingPolicy.FullJson, MaxDepth = 1)]
    public class MaxDepthOneEntity : Entity<MaxDepthOneEntity>
    {
        public string Title { get; set; } = "";
        public NestedObject? Nested { get; set; }
    }

    public class NestedObject
    {
        public string Name { get; set; } = "";
        public DeepNestedObject? DeepNested { get; set; }
    }

    public class DeepNestedObject
    {
        public string Value { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.FullJson)]
    public class CircularReferenceEntity : Entity<CircularReferenceEntity>
    {
        public string Title { get; set; } = "";
        public CircularReferenceEntity? Parent { get; set; }
        public CircularReferenceEntity[]? Children { get; set; }
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, Exclude = new[] { "SecretData" })]
    public class ExclusionEntity : Entity<ExclusionEntity>
    {
        public string PublicData { get; set; } = "";
        public string SecretData { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.FullJson, Exclude = new[] { "SecretData" })]
    public class FullJsonExclusionEntity : Entity<FullJsonExclusionEntity>
    {
        public string PublicData { get; set; } = "";
        public string SecretData { get; set; } = "";
    }

    [Embedding(Template = "")]
    public class EmptyTemplateEntity : Entity<EmptyTemplateEntity>
    {
        public string Title { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, Version = 1)]
    public class VersionedEntity : Entity<VersionedEntity>
    {
        public string Content { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings, Version = 2)]
    public class VersionedEntityV2 : Entity<VersionedEntityV2>
    {
        public string Content { get; set; } = "";
    }

    #endregion
}
