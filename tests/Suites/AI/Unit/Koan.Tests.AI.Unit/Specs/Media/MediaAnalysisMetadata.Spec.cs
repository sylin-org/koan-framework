using Koan.Data.AI;
using Koan.Data.AI.Attributes;

namespace Koan.Tests.AI.Unit.Specs.Media;

/// <summary>
/// Tests for MediaAnalysisMetadata resolution: attribute detection,
/// convention-based property mapping, explicit property overrides, and caching.
/// </summary>
[Trait("ADR", "AI-0027")]
[Trait("Category", "Unit")]
public sealed class MediaAnalysisMetadataSpec
{
    [Fact]
    public void Resolve_returns_metadata_for_annotated_entity()
    {
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<AnnotatedMedia>();

        // Assert
        metadata.Should().NotBeNull();
        metadata!.Analysis.Should().HaveFlag(MediaAnalysis.Describe);
        metadata.Analysis.Should().HaveFlag(MediaAnalysis.Ocr);
        metadata.DescriptionProperty.Should().Be("AiDescription");
        metadata.OcrTextProperty.Should().Be("OcrText");
    }

    [Fact]
    public void Resolve_returns_null_for_unannotated_entity()
    {
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<UnannotatedMedia>();

        // Assert
        metadata.Should().BeNull("no [MediaAnalysis] attribute = null");
    }

    [Fact]
    public void Resolve_detects_convention_property_names()
    {
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<AnnotatedMedia>();

        // Assert — convention detection maps AiDescription and OcrText automatically
        metadata.Should().NotBeNull();
        metadata!.DescriptionProperty.Should().Be("AiDescription",
            "AiDescription is a convention name for Describe output");
        metadata.OcrTextProperty.Should().Be("OcrText",
            "OcrText is a convention name for OCR output");
    }

    [Fact]
    public void Resolve_uses_explicit_property_mapping()
    {
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<ExplicitPropertyMedia>();

        // Assert — ClassificationProperty = "Type" overrides convention
        metadata.Should().NotBeNull();
        metadata!.ClassifyProperty.Should().Be("Type",
            "explicit ClassificationProperty should override convention names like Category");
    }

    [Fact]
    public void Resolve_caches_per_type()
    {
        // Act
        var first = MediaAnalysisMetadata.Resolve<AnnotatedMedia>();
        var second = MediaAnalysisMetadata.Resolve<AnnotatedMedia>();

        // Assert — same reference returned from cache
        first.Should().BeSameAs(second, "metadata should be cached and reused per type");
    }

    [Fact]
    public void Resolve_populates_async_and_version_from_attribute()
    {
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<AnnotatedMedia>();

        // Assert — defaults from attribute
        metadata.Should().NotBeNull();
        metadata!.Async.Should().BeTrue("default Async is true");
        metadata.Version.Should().Be(1, "default Version is 1");
    }

    [Fact]
    public void Resolve_returns_null_properties_when_no_matching_property_exists()
    {
        // Arrange — MinimalMedia has no convention-named properties
        // Act
        var metadata = MediaAnalysisMetadata.Resolve<MinimalMedia>();

        // Assert
        metadata.Should().NotBeNull();
        metadata!.Analysis.Should().Be(MediaAnalysis.Describe);
        metadata.DescriptionProperty.Should().BeNull("no convention property found");
        metadata.OcrTextProperty.Should().BeNull("no OCR property on entity");
    }

    #region Test Entities

    [MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr)]
    private class AnnotatedMedia
    {
        public string? AiDescription { get; set; }
        public string? OcrText { get; set; }
        public byte[] Data { get; set; } = [];
    }

    private class UnannotatedMedia
    {
        public string Title { get; set; } = "";
    }

    [MediaAnalysis(Analysis = MediaAnalysis.Classify, ClassificationProperty = "Type")]
    private class ExplicitPropertyMedia
    {
        public string? Type { get; set; }
        public string? Category { get; set; } // convention name, but explicit takes priority
        public byte[] Content { get; set; } = [];
    }

    [MediaAnalysis(Analysis = MediaAnalysis.Describe)]
    private class MinimalMedia
    {
        public int Size { get; set; }
        public byte[] Payload { get; set; } = [];
    }

    #endregion
}
