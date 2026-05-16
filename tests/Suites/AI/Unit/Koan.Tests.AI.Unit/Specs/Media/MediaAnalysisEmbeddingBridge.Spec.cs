using Koan.Data.AI;
using Koan.Data.AI.Attributes;

namespace Koan.Tests.AI.Unit.Specs.Media;

/// <summary>
/// Tests for MediaAnalysisEmbeddingBridge — composing embedding text
/// from media analysis result properties in deterministic order.
/// </summary>
[Trait("ADR", "AI-0027")]
[Trait("Category", "Unit")]
public sealed class MediaAnalysisEmbeddingBridgeSpec
{
    [Fact]
    public void ComposeEmbeddingText_concatenates_in_deterministic_order()
    {
        // Arrange — Describe before Ocr before Classify
        var entity = new BridgeTestEntity
        {
            AiDescription = "A photo of a receipt",
            OcrText = "Total: $42.00",
            Category = "receipt",
        };
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr | MediaAnalysis.Classify,
            DescriptionProperty = nameof(BridgeTestEntity.AiDescription),
            OcrTextProperty = nameof(BridgeTestEntity.OcrText),
            ClassifyProperty = nameof(BridgeTestEntity.Category),
        };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity, metadata);

        // Assert — order: Description, OCR, Classification
        var descIdx = text.IndexOf("A photo of a receipt", StringComparison.Ordinal);
        var ocrIdx = text.IndexOf("Total: $42.00", StringComparison.Ordinal);
        var classIdx = text.IndexOf("receipt", ocrIdx + 1, StringComparison.Ordinal);

        descIdx.Should().BeGreaterThanOrEqualTo(0, "description should be present");
        ocrIdx.Should().BeGreaterThan(descIdx, "OCR should come after description");
        classIdx.Should().BeGreaterThan(ocrIdx, "classification should come after OCR");
    }

    [Fact]
    public void ComposeEmbeddingText_skips_null_properties()
    {
        // Arrange — OcrText is null
        var entity = new BridgeTestEntity
        {
            AiDescription = "A landscape photo",
            OcrText = null,
            Category = "nature",
        };
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr | MediaAnalysis.Classify,
            DescriptionProperty = nameof(BridgeTestEntity.AiDescription),
            OcrTextProperty = nameof(BridgeTestEntity.OcrText),
            ClassifyProperty = nameof(BridgeTestEntity.Category),
        };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity, metadata);

        // Assert
        text.Should().Contain("A landscape photo");
        text.Should().Contain("nature");
        text.Should().NotContain("OCR:", "null OCR property should be skipped entirely");
    }

    [Fact]
    public void ComposeEmbeddingText_skips_empty_properties()
    {
        // Arrange — whitespace-only values should be omitted
        var entity = new BridgeTestEntity
        {
            AiDescription = "   ",
            OcrText = "",
            Category = "document",
        };
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr | MediaAnalysis.Classify,
            DescriptionProperty = nameof(BridgeTestEntity.AiDescription),
            OcrTextProperty = nameof(BridgeTestEntity.OcrText),
            ClassifyProperty = nameof(BridgeTestEntity.Category),
        };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity, metadata);

        // Assert
        text.Should().NotContain("Description:", "whitespace-only description should be skipped");
        text.Should().NotContain("OCR:", "empty OCR text should be skipped");
        text.Should().Contain("document", "non-empty classification should be present");
    }

    [Fact]
    public void ComposeEmbeddingText_returns_empty_when_no_properties_populated()
    {
        // Arrange — all null
        var entity = new BridgeTestEntity
        {
            AiDescription = null,
            OcrText = null,
            Category = null,
        };
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr | MediaAnalysis.Classify,
            DescriptionProperty = nameof(BridgeTestEntity.AiDescription),
            OcrTextProperty = nameof(BridgeTestEntity.OcrText),
            ClassifyProperty = nameof(BridgeTestEntity.Category),
        };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity, metadata);

        // Assert
        text.Should().BeEmpty("all properties null = empty string");
    }

    [Fact]
    public void ComposeText_without_metadata_resolves_from_attribute()
    {
        // Arrange
        var entity = new AttributedBridgeEntity
        {
            AiDescription = "Test description",
            OcrText = "Test OCR",
        };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity);

        // Assert
        text.Should().Contain("Test description");
        text.Should().Contain("Test OCR");
    }

    [Fact]
    public void ComposeText_for_unannotated_entity_returns_empty()
    {
        // Arrange
        var entity = new PlainEntity { Name = "not annotated" };

        // Act
        var text = MediaAnalysisEmbeddingBridge.ComposeText(entity);

        // Assert
        text.Should().BeEmpty("entity without [MediaAnalysis] produces empty text");
    }

    #region Test Entities

    private class BridgeTestEntity
    {
        public string? AiDescription { get; set; }
        public string? OcrText { get; set; }
        public string? Category { get; set; }
    }

    [MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr)]
    private class AttributedBridgeEntity
    {
        public string? AiDescription { get; set; }
        public string? OcrText { get; set; }
    }

    private class PlainEntity
    {
        public string? Name { get; set; }
    }

    #endregion
}
