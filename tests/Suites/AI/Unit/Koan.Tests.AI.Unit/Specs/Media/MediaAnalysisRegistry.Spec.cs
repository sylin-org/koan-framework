using Koan.Data.AI;
using Koan.Data.AI.Attributes;

namespace Koan.Tests.AI.Unit.Specs.Media;

/// <summary>
/// Tests for MediaAnalysisRegistry — type registration and async filtering.
/// Each test resets the global registry to avoid cross-test pollution.
/// </summary>
[Trait("ADR", "AI-0027")]
[Trait("Category", "Unit")]
public sealed class MediaAnalysisRegistrySpec : IDisposable
{
    public MediaAnalysisRegistrySpec()
    {
        // Reset before each test to guarantee clean state
        MediaAnalysisRegistry.ResetForTesting();
    }

    public void Dispose()
    {
        MediaAnalysisRegistry.ResetForTesting();
    }

    [Fact]
    public void Register_adds_type()
    {
        // Arrange & Act
        MediaAnalysisRegistry.Register(typeof(AsyncPhoto), async: true);

        // Assert
        MediaAnalysisRegistry.GetRegisteredTypes()
            .Should().Contain(typeof(AsyncPhoto));
    }

    [Fact]
    public void AsyncEntityTypes_filters_by_metadata()
    {
        // Arrange — register both async and sync types
        MediaAnalysisRegistry.Register(typeof(AsyncPhoto), async: true);
        MediaAnalysisRegistry.Register(typeof(SyncDocument), async: false);

        // Act
        var asyncTypes = MediaAnalysisRegistry.AsyncEntityTypes.ToList();

        // Assert — only types whose [MediaAnalysis] Async=true via metadata
        asyncTypes.Should().Contain(typeof(AsyncPhoto),
            "AsyncPhoto has Async=true in attribute");
        asyncTypes.Should().NotContain(typeof(SyncDocument),
            "SyncDocument has Async=false in attribute");
    }

    [Fact]
    public void GetRegisteredTypes_returns_empty_initially()
    {
        // Act
        var types = MediaAnalysisRegistry.GetRegisteredTypes();

        // Assert
        types.Should().BeEmpty("registry should start clean after reset");
    }

    [Fact]
    public void RegisterTypes_adds_multiple_types()
    {
        // Arrange & Act
        MediaAnalysisRegistry.RegisterTypes([typeof(AsyncPhoto), typeof(SyncDocument)]);

        // Assert
        var registered = MediaAnalysisRegistry.GetRegisteredTypes();
        registered.Should().HaveCount(2);
        registered.Should().Contain(typeof(AsyncPhoto));
        registered.Should().Contain(typeof(SyncDocument));
    }

    [Fact]
    public void Register_ignores_duplicate_types()
    {
        // Arrange
        MediaAnalysisRegistry.Register(typeof(AsyncPhoto), async: true);

        // Act
        MediaAnalysisRegistry.Register(typeof(AsyncPhoto), async: true);

        // Assert
        MediaAnalysisRegistry.GetRegisteredTypes()
            .Should().HaveCount(1, "duplicate registration should not add twice");
    }

    [Fact]
    public void RegisterTypes_ignores_null_entries()
    {
        // Act — collection with nulls
        MediaAnalysisRegistry.RegisterTypes([typeof(AsyncPhoto), null!]);

        // Assert
        MediaAnalysisRegistry.GetRegisteredTypes()
            .Should().HaveCount(1, "null entries should be skipped");
    }

    #region Test Entities

    [MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Async = true)]
    private class AsyncPhoto
    {
        public string? AiDescription { get; set; }
        public string? OcrText { get; set; }
    }

    [MediaAnalysis(Analysis = MediaAnalysis.Describe, Async = false)]
    private class SyncDocument
    {
        public string? AiDescription { get; set; }
    }

    #endregion
}
